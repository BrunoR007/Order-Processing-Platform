using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderProcessing.Messaging;
using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Infrastructure;
using Xunit;

namespace OrderProcessing.Platform.IntegrationTests;

[Collection("postgres")]
public sealed class OrderPersistenceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Failed_outbox_write_does_not_leave_an_order_without_its_event()
    {
        var connectionString = await fixture.CreateDatabase();
        await using var db = new OrdersDb(PostgresFixture.Options<OrdersDb>(connectionString));
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION reject_outbox() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'simulated outbox write failure'; END $$;
            CREATE TRIGGER reject_outbox BEFORE INSERT ON outbox FOR EACH ROW EXECUTE FUNCTION reject_outbox();
            """);
        await Assert.ThrowsAsync<DbUpdateException>(() => new OrderStore(db).Create(
            new([new("BOOK-DOTNET", 1)]), Guid.NewGuid().ToString(), Guid.NewGuid(), default));
        db.ChangeTracker.Clear();
        Assert.Equal(0, await db.Orders.CountAsync());
        Assert.Equal(0, await db.Outbox.CountAsync());
    }

    [Fact]
    public async Task Http_creation_is_atomic_idempotent_and_uses_server_prices()
    {
        var connectionString = await fixture.CreateDatabase();
        await using (var schema = new OrdersDb(PostgresFixture.Options<OrdersDb>(connectionString)))
            await schema.Database.MigrateAsync();
        await using var factory = new WebApplicationFactory<Orders.Api.Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<OrdersDb>>();
                services.AddDbContext<OrdersDb>(options => options.UseNpgsql(connectionString));
            });
        });
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var correlation = Guid.NewGuid();
        client.DefaultRequestHeaders.Add("X-Correlation-Id", correlation.ToString());
        var request = new CreateOrderRequest([new("BOOK-DOTNET", 2)]);
        using var created = await client.PostAsJsonAsync("/orders", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var body = await created.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal(59.80m, body.GetProperty("amount").GetDecimal());
        Assert.Equal(correlation, body.GetProperty("correlationId").GetGuid());
        using var repeated = await client.PostAsJsonAsync("/orders", request);
        Assert.Equal(HttpStatusCode.OK, repeated.StatusCode);
        using var conflicting = await client.PostAsJsonAsync("/orders", new CreateOrderRequest([new("BOOK-DOTNET", 1)]));
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        using var fetched = await client.GetAsync(created.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        await using var db = new OrdersDb(PostgresFixture.Options<OrdersDb>(connectionString));
        Assert.Equal(1, await db.Orders.CountAsync());
        var message = Assert.Single(await db.Outbox.ToListAsync());
        Assert.Null(message.DispatchedAt);
        var envelope = System.Text.Json.JsonSerializer.Deserialize<EventEnvelope>(message.Payload, EventEnvelope.Json)!;
        Assert.Equal(correlation, envelope.CorrelationId);
    }

    [Fact]
    public async Task Concurrent_same_request_creates_one_order_and_one_outbox_message()
    {
        var connectionString = await fixture.CreateDatabase();
        await using var schema = new OrdersDb(PostgresFixture.Options<OrdersDb>(connectionString));
        await schema.Database.MigrateAsync();
        var key = Guid.NewGuid().ToString();
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var db = new OrdersDb(PostgresFixture.Options<OrdersDb>(connectionString));
            return await new OrderStore(db).Create(new([new("MUG-EVENTS", 1)]), key, Guid.NewGuid(), default);
        }));
        Assert.Single(results, x => x.Created);
        Assert.Single(results.Select(x => x.Order.Id).Distinct());
        Assert.Equal(1, await schema.Outbox.CountAsync());
    }
}
