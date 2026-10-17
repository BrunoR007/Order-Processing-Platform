using System.Diagnostics;
using Microsoft.EntityFrameworkCore;

namespace OrderProcessing.Messaging;

public interface IEventHandler
{
    Task Handle(EventEnvelope envelope, CancellationToken cancellationToken);
}

public static class InboxProcessor
{
    public static readonly ActivitySource Activities = new("OrderProcessing.Messaging");

    public static async Task<bool> Process(MessageDbContext db, string consumer,
        EventEnvelope envelope, IEventHandler handler, CancellationToken ct)
    {
        ActivityContext.TryParse(envelope.TraceParent, envelope.TraceState, isRemote: true, out var parent);
        using var activity = Activities.StartActivity($"{envelope.Type} process", ActivityKind.Consumer, parent);
        activity?.SetTag("messaging.message.id", envelope.MessageId);
        activity?.SetTag("correlation.id", envelope.CorrelationId);
        activity?.SetTag("messaging.destination.name", consumer);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // The unique key serializes concurrent duplicates. Rollback removes the claim on failure.
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO inbox ("Consumer", "MessageId", "ProcessedAt")
            VALUES ({consumer}, {envelope.MessageId}, {DateTimeOffset.UtcNow})
            ON CONFLICT DO NOTHING
            """, ct);
        if (inserted == 0)
        {
            MessageTelemetry.Duplicates.Add(1);
            await transaction.CommitAsync(ct);
            return false;
        }
        try
        {
            await handler.Handle(envelope, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            MessageTelemetry.Processed.Add(1, new KeyValuePair<string, object?>("event.type", envelope.Type));
            return true;
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
    }
}
