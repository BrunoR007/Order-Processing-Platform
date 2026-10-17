using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OrderProcessing.Contracts;
using OrderProcessing.Messaging;

namespace OrderProcessing.Inventory.Service;

public sealed class InventoryEventHandler(InventoryDb db) : IEventHandler
{
    public async Task Handle(EventEnvelope envelope, CancellationToken ct)
    {
        if (envelope.Type == EventTypes.OrderCreated)
        {
            var order = envelope.Read<OrderCreated>();
            // Serialize different message IDs carrying the same business operation as well.
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({order.OrderId.ToString()}, 0))", ct);
            if (await db.Reservations.AnyAsync(x => x.OrderId == order.OrderId, ct)) return;
            if (order.Lines is not { Length: > 0 } || order.Lines.Any(x => x.Quantity <= 0))
                throw new InvalidOperationException("Invalid reservation payload.");
            var transaction = db.Database.CurrentTransaction!;
            await transaction.CreateSavepointAsync("reserve_stock", ct);
            var reserved = true;
            foreach (var line in order.Lines.OrderBy(x => x.Sku, StringComparer.Ordinal))
            {
                var changed = await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE stock SET "Available" = "Available" - {line.Quantity}
                    WHERE "Sku" = {line.Sku} AND "Available" >= {line.Quantity}
                    """, ct);
                if (changed == 1) continue;
                reserved = false;
                await transaction.RollbackToSavepointAsync("reserve_stock", ct);
                break;
            }
            db.Reservations.Add(new Reservation
            {
                OrderId = order.OrderId,
                LinesJson = JsonSerializer.Serialize(order.Lines, EventEnvelope.Json),
                Status = reserved ? "Reserved" : "Rejected"
            });
            db.Enqueue(reserved
                ? EventEnvelope.Create(EventTypes.StockReserved, new StockReserved(order.OrderId, order.Amount, order.Currency), envelope)
                : EventEnvelope.Create(EventTypes.StockReservationFailed, new OrderOutcome(order.OrderId, "Insufficient stock"), envelope));
        }
        else if (envelope.Type == EventTypes.PaymentFailed)
        {
            var outcome = envelope.Read<OrderOutcome>();
            var reservation = await db.Reservations.FromSqlInterpolated($"""
                SELECT * FROM reservations WHERE "OrderId" = {outcome.OrderId} FOR UPDATE
                """).SingleAsync(ct);
            if (reservation.Status != "Reserved") return;
            var lines = JsonSerializer.Deserialize<OrderLine[]>(reservation.LinesJson, EventEnvelope.Json)!;
            foreach (var line in lines.OrderBy(x => x.Sku, StringComparer.Ordinal))
                await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE stock SET "Available" = "Available" + {line.Quantity} WHERE "Sku" = {line.Sku}
                    """, ct);
            reservation.Status = "Released";
            db.Enqueue(EventEnvelope.Create(EventTypes.StockReleased, outcome, envelope));
        }
        else throw new InvalidOperationException("Unsupported inventory event.");
    }
}
