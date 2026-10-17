using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using OrderProcessing.Contracts;
using OrderProcessing.Messaging;
using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Infrastructure;
using OrderProcessing.Payments.Service;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using Xunit;

namespace OrderProcessing.Platform.IntegrationTests;

[Collection("messaging")]
public sealed class MessagingReliabilityTests(PostgresFixture postgres, BrokerFixture rabbit)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Invalid_delivery_keeps_original_bytes_and_identity_in_dead_letters(bool overflowingAttempt)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var ct = timeout.Token;
        var connectionString = await postgres.CreateDatabase();
        await using var db = new PaymentsDb(PostgresFixture.Options<PaymentsDb>(connectionString));
        await db.Database.MigrateAsync(ct);
        var config = rabbit.Configuration();
        await using var broker = new RabbitBroker(config);
        await using var channel = await broker.OpenChannel(ct);
        await Purge(channel, ct);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddDbContext<PaymentsDb>(options => options.UseNpgsql(connectionString));
        services.AddScoped<PaymentEventHandler>();
        await using var provider = services.BuildServiceProvider();
        using var consumer = new MessageConsumer<PaymentsDb, PaymentEventHandler>(provider.GetRequiredService<IServiceScopeFactory>(),
            broker, new("payments"), NullLogger<MessageConsumer<PaymentsDb, PaymentEventHandler>>.Instance);
        await consumer.StartAsync(ct);
        try
        {
            var envelope = EventEnvelope.Create(EventTypes.StockReserved, new StockReserved(Guid.NewGuid(), 10, "USD"))
                with
            { Attempt = int.MaxValue };
            var body = overflowingAttempt ? JsonSerializer.SerializeToUtf8Bytes(envelope, EventEnvelope.Json)
                : Encoding.UTF8.GetBytes("{broken-json");
            var traceParent = $"00-{ActivityTraceId.CreateRandom()}-{ActivitySpanId.CreateRandom()}-01";
            await channel.BasicPublishAsync("", "payments", true, new BasicProperties
            {
                Persistent = true,
                MessageId = envelope.MessageId.ToString(),
                CorrelationId = envelope.CorrelationId.ToString(),
                ContentType = "application/json",
                Expiration = "15000",
                Headers = new Dictionary<string, object?> { ["traceparent"] = traceParent }
            }, body, ct);
            BasicGetResult? dead = null;
            while (dead is null)
            {
                await Task.Delay(50, ct);
                dead = await channel.BasicGetAsync("payments.dead", true, ct);
            }
            Assert.Equal(body, dead.Body.ToArray());
            Assert.Equal(envelope.MessageId.ToString(), dead.BasicProperties.MessageId);
            Assert.Equal(envelope.CorrelationId.ToString(), dead.BasicProperties.CorrelationId);
            Assert.Equal(traceParent, Encoding.UTF8.GetString((byte[])dead.BasicProperties.Headers!["traceparent"]!));
            Assert.Equal("JsonException", Encoding.UTF8.GetString((byte[])dead.BasicProperties.Headers["failure-type"]!));
            Assert.Null(dead.BasicProperties.Expiration);
            Assert.Equal(0, await db.Inbox.CountAsync(ct));
            Assert.Equal(0, await db.Payments.CountAsync(ct));
        }
        finally { await consumer.StopAsync(CancellationToken.None); }
    }

    [Fact]
    public async Task Unroutable_publish_fails_and_marks_the_producer_trace_as_error()
    {
        Activity? published = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "OrderProcessing.Messaging",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => published = activity
        };
        ActivitySource.AddActivityListener(listener);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        await using var broker = new RabbitBroker(rabbit.Configuration());
        await using var channel = await broker.OpenChannel(timeout.Token);
        var envelope = EventEnvelope.Create(EventTypes.StockReserved, new StockReserved(Guid.NewGuid(), 10, "USD"));
        var exception = await Assert.ThrowsAsync<PublishException>(() => RabbitBroker.Publish(channel,
            envelope, $"missing-{Guid.NewGuid():N}", timeout.Token));
        Assert.True(exception.IsReturn);
        Assert.NotNull(published);
        Assert.Equal(ActivityStatusCode.Error, published.Status);
    }

    [Fact]
    public async Task Publish_before_database_crash_redelivers_same_message_id()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var ct = timeout.Token;
        var connectionString = await postgres.CreateDatabase();
        var services = new ServiceCollection();
        services.AddDbContext<OrdersDb>(options => options.UseNpgsql(connectionString));
        await using var provider = services.BuildServiceProvider();
        await using var db = new OrdersDb(PostgresFixture.Options<OrdersDb>(connectionString));
        await db.Database.MigrateAsync(ct);
        await new OrderStore(db).Create(new([new("BOOK-DOTNET", 1)]), Guid.NewGuid().ToString(), Guid.NewGuid(), ct);
        await using var broker = new RabbitBroker(rabbit.Configuration());
        await using var channel = await broker.OpenChannel(ct);
        await Purge(channel, ct);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_dispatch() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'simulated crash after publish'; END $$;
            CREATE TRIGGER fail_dispatch BEFORE UPDATE ON outbox FOR EACH ROW EXECUTE FUNCTION fail_dispatch();
            """, ct);
        await Assert.ThrowsAsync<DbUpdateException>(() => OutboxRelay<OrdersDb>.DispatchBatch(
            provider.GetRequiredService<IServiceScopeFactory>(), channel, ct));
        db.ChangeTracker.Clear();
        Assert.Null((await db.Outbox.SingleAsync(ct)).DispatchedAt);
        await db.Database.ExecuteSqlRawAsync("DROP TRIGGER fail_dispatch ON outbox", ct);
        await OutboxRelay<OrdersDb>.DispatchBatch(provider.GetRequiredService<IServiceScopeFactory>(), channel, ct);
        var first = await channel.BasicGetAsync("inventory", true, ct);
        var second = await channel.BasicGetAsync("inventory", true, ct);
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(first.BasicProperties.MessageId, second.BasicProperties.MessageId);
        Assert.Equal((await db.Outbox.AsNoTracking().SingleAsync(ct)).Id.ToString(), first.BasicProperties.MessageId);
        Assert.NotNull((await db.Outbox.AsNoTracking().SingleAsync(ct)).DispatchedAt);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task Transient_failure_retries_or_dead_letters_and_can_be_replayed(int failures)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = timeout.Token;
        var connectionString = await postgres.CreateDatabase();
        await using var schema = new PaymentsDb(PostgresFixture.Options<PaymentsDb>(connectionString));
        await schema.Database.MigrateAsync(ct);
        var config = rabbit.Configuration(failures);
        await using var broker = new RabbitBroker(config);
        await using var channel = await broker.OpenChannel(ct);
        await Purge(channel, ct);
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(config);
        services.AddDbContext<PaymentsDb>(options => options.UseNpgsql(connectionString));
        services.AddScoped<PaymentEventHandler>();
        await using var provider = services.BuildServiceProvider();
        using var consumer = new MessageConsumer<PaymentsDb, PaymentEventHandler>(provider.GetRequiredService<IServiceScopeFactory>(),
            broker, new("payments"), NullLogger<MessageConsumer<PaymentsDb, PaymentEventHandler>>.Instance);
        await consumer.StartAsync(ct);
        var envelope = EventEnvelope.Create(EventTypes.StockReserved, new StockReserved(Guid.NewGuid(), 10, "USD"));
        await RabbitBroker.Publish(channel, envelope, "payments", ct);
        try
        {
            if (failures > 3)
            {
                BasicGetResult? dead = null;
                while (dead is null)
                {
                    await Task.Delay(150, ct);
                    dead = await channel.BasicGetAsync("payments.dead", false, ct);
                }
                var saved = JsonSerializer.Deserialize<EventEnvelope>(dead.Body.Span, EventEnvelope.Json)!;
                Assert.Equal(envelope.MessageId, saved.MessageId);
                Assert.Equal(4, saved.Attempt);
                Assert.Equal("IOException", saved.LastError);
                Assert.Equal(0, await schema.Inbox.CountAsync(ct));
                config["Payments:TransientFailuresBeforeSuccess"] = "0";
                await RabbitBroker.Publish(channel, saved with { Attempt = 0, LastError = null }, "payments", ct);
                await channel.BasicAckAsync(dead.DeliveryTag, false, ct);
            }
            while (!await schema.Payments.AnyAsync(ct)) await Task.Delay(100, ct);
            Assert.Equal(1, await schema.Payments.CountAsync(ct));
            Assert.Equal(envelope.MessageId, (await schema.Inbox.SingleAsync(ct)).MessageId);
            Assert.Equal(1, await schema.Outbox.CountAsync(ct));
        }
        finally { await consumer.StopAsync(CancellationToken.None); }
    }

    internal static async Task Purge(IChannel channel, CancellationToken ct)
    {
        foreach (var service in RabbitBroker.Subscriptions.Keys)
        {
            await channel.QueuePurgeAsync(service, ct);
            await channel.QueuePurgeAsync($"{service}.dead", ct);
            for (var attempt = 1; attempt <= 3; attempt++) await channel.QueuePurgeAsync($"{service}.retry.{attempt}", ct);
        }
    }
}
