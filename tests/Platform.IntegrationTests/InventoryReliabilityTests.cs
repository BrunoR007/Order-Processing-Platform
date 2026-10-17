using Microsoft.EntityFrameworkCore;
using OrderProcessing.Contracts;
using OrderProcessing.Inventory.Service;
using OrderProcessing.Messaging;
using Xunit;

namespace OrderProcessing.Platform.IntegrationTests;

[Collection("postgres")]
public sealed class InventoryReliabilityTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Concurrent_duplicate_deliveries_reserve_once()
    {
        var connectionString = await fixture.CreateDatabase();
        await using var db = new InventoryDb(PostgresFixture.Options<InventoryDb>(connectionString));
        await db.Database.MigrateAsync();
        var envelope = Created(1);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(async _ =>
        {
            await using var consumerDb = new InventoryDb(PostgresFixture.Options<InventoryDb>(connectionString));
            return await InboxProcessor.Process(consumerDb, "inventory", envelope, new InventoryEventHandler(consumerDb), default);
        }));
        Assert.Single(results, x => x);
        Assert.Equal(49, (await db.Stock.SingleAsync(x => x.Sku == "BOOK-DOTNET")).Available);
        Assert.Equal(1, await db.Inbox.CountAsync());
        Assert.Equal(1, await db.Outbox.CountAsync());
    }

    [Fact]
    public async Task Competing_orders_do_not_oversell_and_compensation_releases_once()
    {
        var connectionString = await fixture.CreateDatabase();
        await using var db = new InventoryDb(PostgresFixture.Options<InventoryDb>(connectionString));
        await db.Database.MigrateAsync();
        await db.Stock.Where(x => x.Sku == "BOOK-DOTNET").ExecuteUpdateAsync(update => update.SetProperty(x => x.Available, 1));
        var requests = new[] { Created(1), Created(1) };
        await Task.WhenAll(requests.Select(async envelope =>
        {
            await using var consumerDb = new InventoryDb(PostgresFixture.Options<InventoryDb>(connectionString));
            await InboxProcessor.Process(consumerDb, "inventory", envelope, new InventoryEventHandler(consumerDb), default);
        }));
        Assert.Equal(0, (await db.Stock.SingleAsync(x => x.Sku == "BOOK-DOTNET")).Available);
        var reservation = await db.Reservations.SingleAsync(x => x.Status == "Reserved");
        for (var index = 0; index < 2; index++)
        {
            await using var consumerDb = new InventoryDb(PostgresFixture.Options<InventoryDb>(connectionString));
            var failed = EventEnvelope.Create(EventTypes.PaymentFailed, new OrderOutcome(reservation.OrderId, "Declined"));
            await InboxProcessor.Process(consumerDb, "inventory", failed, new InventoryEventHandler(consumerDb), default);
        }
        db.ChangeTracker.Clear();
        Assert.Equal(1, (await db.Stock.SingleAsync(x => x.Sku == "BOOK-DOTNET")).Available);
        Assert.Equal("Released", (await db.Reservations.SingleAsync(x => x.OrderId == reservation.OrderId)).Status);
        Assert.Equal(3, await db.Outbox.CountAsync());
    }

    [Fact]
    public async Task Failed_multi_line_reservation_rolls_back_partial_stock_changes()
    {
        var connectionString = await fixture.CreateDatabase();
        await using var db = new InventoryDb(PostgresFixture.Options<InventoryDb>(connectionString));
        await db.Database.MigrateAsync();
        var envelope = EventEnvelope.Create(EventTypes.OrderCreated,
            new OrderCreated(Guid.NewGuid(), [new("BOOK-DOTNET", 1), new("UNKNOWN", 1)], 10, "USD"));
        await InboxProcessor.Process(db, "inventory", envelope, new InventoryEventHandler(db), default);
        Assert.Equal(50, (await db.Stock.SingleAsync(x => x.Sku == "BOOK-DOTNET")).Available);
        Assert.Equal("Rejected", (await db.Reservations.SingleAsync()).Status);
    }

    private static EventEnvelope Created(int quantity) => EventEnvelope.Create(EventTypes.OrderCreated,
        new OrderCreated(Guid.NewGuid(), [new("BOOK-DOTNET", quantity)], 29.90m * quantity, "USD"));
}
