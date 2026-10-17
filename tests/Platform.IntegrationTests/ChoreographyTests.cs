using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OrderProcessing.Inventory.Service;
using OrderProcessing.Messaging;
using OrderProcessing.Notifications.Service;
using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Domain;
using OrderProcessing.Orders.Infrastructure;
using Xunit;

namespace OrderProcessing.Platform.IntegrationTests;

[Collection("messaging")]
public sealed class ChoreographyTests(PostgresFixture postgres, BrokerFixture rabbit)
{
    [Theory]
    [InlineData("0", OrderStatus.Confirmed, 49)]
    [InlineData("1", OrderStatus.Cancelled, 50)]
    public async Task Order_reaches_terminal_state_and_notifies_after_stock_consistency(string failureRate, OrderStatus expected, int available)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        var config = rabbit.Configuration(rate: failureRate);
        await using (var broker = new RabbitBroker(config))
        await using (var channel = await broker.OpenChannel(ct))
            await MessagingReliabilityTests.Purge(channel, ct);
        var ordersConnection = await postgres.CreateDatabase();
        var inventoryConnection = await postgres.CreateDatabase();
        var paymentsConnection = await postgres.CreateDatabase();
        var notificationsConnection = await postgres.CreateDatabase();
        await using var inventory = Factory<Inventory.Service.Program, InventoryDb>(inventoryConnection, config);
        using var inventoryClient = inventory.CreateClient();
        await using var payments = Factory<Payments.Service.Program, Payments.Service.PaymentsDb>(paymentsConnection, config);
        using var paymentsClient = payments.CreateClient();
        await using var notifications = Factory<Notifications.Service.Program, NotificationsDb>(notificationsConnection, config);
        using var notificationsClient = notifications.CreateClient();
        await using var orders = Factory<Orders.Api.Program, OrdersDb>(ordersConnection, config);
        using var client = orders.CreateClient();
        client.DefaultRequestHeaders.Add("Idempotency-Key", Guid.NewGuid().ToString());
        using var response = await client.PostAsJsonAsync("/orders", new CreateOrderRequest([new("BOOK-DOTNET", 1)]), ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
        var id = body.GetProperty("id").GetGuid();
        await using var db = new OrdersDb(PostgresFixture.Options<OrdersDb>(ordersConnection));
        while (!await db.Orders.AnyAsync(x => x.Id == id && x.Status == expected, ct)) await Task.Delay(100, ct);
        await using var inventoryDb = new InventoryDb(PostgresFixture.Options<InventoryDb>(inventoryConnection));
        Assert.Equal(available, (await inventoryDb.Stock.SingleAsync(x => x.Sku == "BOOK-DOTNET", ct)).Available);
        await using var notificationDb = new NotificationsDb(PostgresFixture.Options<NotificationsDb>(notificationsConnection));
        while (!await notificationDb.Notifications.AnyAsync(x => x.OrderId == id, ct)) await Task.Delay(100, ct);
        Assert.Equal(1, await notificationDb.Notifications.CountAsync(ct));
    }

    private static WebApplicationFactory<TProgram> Factory<TProgram, TDb>(string connectionString, IConfiguration config)
        where TProgram : class where TDb : MessageDbContext =>
        new WebApplicationFactory<TProgram>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Integration");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<DbContextOptions<TDb>>();
                services.AddDbContext<TDb>(options => options.UseNpgsql(connectionString));
                services.AddSingleton(config);
                services.AddSingleton(_ => new RabbitBroker(config));
            });
        });
}
