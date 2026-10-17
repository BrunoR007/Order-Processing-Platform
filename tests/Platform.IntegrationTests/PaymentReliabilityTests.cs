using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using OrderProcessing.Contracts;
using OrderProcessing.Messaging;
using OrderProcessing.Payments.Service;
using Xunit;

namespace OrderProcessing.Platform.IntegrationTests;

[Collection("postgres")]
public sealed class PaymentReliabilityTests(PostgresFixture fixture)
{
    [Theory]
    [InlineData("0", true)]
    [InlineData("1", false)]
    public async Task Business_outcome_is_persisted_and_semantic_duplicates_do_not_charge_again(string rate, bool approved)
    {
        var connectionString = await fixture.CreateDatabase();
        await using var db = new PaymentsDb(PostgresFixture.Options<PaymentsDb>(connectionString));
        await db.Database.MigrateAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Payments:FailureRate"] = rate }).Build();
        var envelope = EventEnvelope.Create(EventTypes.StockReserved, new StockReserved(Guid.NewGuid(), 10, "USD"));
        await InboxProcessor.Process(db, "payments", envelope, new PaymentEventHandler(db, config), default);
        db.ChangeTracker.Clear();
        await InboxProcessor.Process(db, "payments", envelope with { MessageId = Guid.NewGuid() }, new PaymentEventHandler(db, config), default);
        Assert.Equal(approved, (await db.Payments.SingleAsync()).Approved);
        Assert.Equal(1, await db.Outbox.CountAsync());
    }

    [Fact]
    public async Task Technical_failure_rolls_back_inbox_and_retry_can_commit()
    {
        var connectionString = await fixture.CreateDatabase();
        await using var db = new PaymentsDb(PostgresFixture.Options<PaymentsDb>(connectionString));
        await db.Database.MigrateAsync();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Payments:TransientFailuresBeforeSuccess"] = "1",
            ["Payments:FailureRate"] = "0"
        }).Build();
        var envelope = EventEnvelope.Create(EventTypes.StockReserved, new StockReserved(Guid.NewGuid(), 10, "USD"));
        await Assert.ThrowsAsync<IOException>(() => InboxProcessor.Process(db, "payments", envelope, new PaymentEventHandler(db, config), default));
        Assert.Equal(0, await db.Inbox.CountAsync());
        Assert.Equal(0, await db.Outbox.CountAsync());
        Assert.Equal(0, await db.Payments.CountAsync());
        await InboxProcessor.Process(db, "payments", envelope with { Attempt = 1 }, new PaymentEventHandler(db, config), default);
        Assert.Equal(1, await db.Inbox.CountAsync());
        Assert.Equal(1, await db.Outbox.CountAsync());
    }
}
