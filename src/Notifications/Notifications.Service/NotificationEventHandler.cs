using Microsoft.EntityFrameworkCore;
using OrderProcessing.Contracts;
using OrderProcessing.Messaging;

namespace OrderProcessing.Notifications.Service;

public sealed class NotificationEventHandler(NotificationsDb db, ILogger<NotificationEventHandler> logger) : IEventHandler
{
    public async Task Handle(EventEnvelope envelope, CancellationToken ct)
    {
        var outcome = envelope.Read<OrderOutcome>();
        if (envelope.Type is not (EventTypes.OrderConfirmed or EventTypes.OrderCancelled))
            throw new InvalidOperationException("Unsupported notification event.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({outcome.OrderId.ToString()}, 0))", ct);
        if (await db.Notifications.AnyAsync(x => x.OrderId == outcome.OrderId, ct)) return;
        db.Notifications.Add(new Notification { OrderId = outcome.OrderId, Outcome = envelope.Type, RecordedAt = DateTimeOffset.UtcNow });
        // Logs are a simulated external side effect: a crash before commit can repeat this line.
        logger.LogInformation("Simulated notification: order {OrderId}, outcome {Outcome}, correlation {CorrelationId}",
            outcome.OrderId, envelope.Type, envelope.CorrelationId);
    }
}
