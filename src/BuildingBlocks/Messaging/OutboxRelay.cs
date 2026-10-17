using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OrderProcessing.Messaging;

public sealed class OutboxRelay<TDb>(IServiceScopeFactory scopes, RabbitBroker broker,
    ILogger<OutboxRelay<TDb>> logger) : BackgroundService where TDb : MessageDbContext
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Refresh even when the broker is offline, so the backlog metric remains useful.
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<TDb>();
                    Interlocked.Exchange(ref MessageTelemetry.PendingOutbox,
                        await db.Outbox.CountAsync(x => x.DispatchedAt == null, stoppingToken));
                }
                await using var channel = await broker.OpenChannel(stoppingToken);
                while (channel.IsOpen && !stoppingToken.IsCancellationRequested)
                {
                    if (!await DispatchBatch(scopes, channel, stoppingToken))
                        await Task.Delay(250, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Outbox relay unavailable; persisted messages remain pending");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }

    public static async Task<bool> DispatchBatch(IServiceScopeFactory scopes,
        RabbitMQ.Client.IChannel channel, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDb>();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        Interlocked.Exchange(ref MessageTelemetry.PendingOutbox, await db.Outbox.CountAsync(x => x.DispatchedAt == null, ct));
        // A second relay skips locked rows. Locks are released on crash/rollback.
        var batch = await db.Outbox.FromSqlRaw("""
            SELECT * FROM outbox WHERE "DispatchedAt" IS NULL
            ORDER BY "CreatedAt" LIMIT 20 FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct);
        foreach (var row in batch)
        {
            var envelope = JsonSerializer.Deserialize<EventEnvelope>(row.Payload, EventEnvelope.Json)
                ?? throw new JsonException("Invalid outbox envelope.");
            await RabbitBroker.Publish(channel, envelope, null, ct);
            // Confirms precede the database commit: a crash here causes safe redelivery.
            row.DispatchedAt = DateTimeOffset.UtcNow;
            MessageTelemetry.OutboxLatency.Record((row.DispatchedAt.Value - row.CreatedAt).TotalSeconds);
        }
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return batch.Count > 0;
    }
}
