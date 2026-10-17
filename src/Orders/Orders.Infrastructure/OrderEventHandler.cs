using Microsoft.EntityFrameworkCore;
using OrderProcessing.Contracts;
using OrderProcessing.Messaging;
using OrderProcessing.Orders.Domain;

namespace OrderProcessing.Orders.Infrastructure;

public sealed class OrderEventHandler(OrdersDb db) : IEventHandler
{
    public async Task Handle(EventEnvelope envelope, CancellationToken ct)
    {
        var outcome = envelope.Type == EventTypes.StockReserved
            ? new OrderOutcome(envelope.Read<StockReserved>().OrderId) : envelope.Read<OrderOutcome>();
        // Row locks also serialize distinct messages for the same order across replicas.
        var order = await db.Orders.FromSqlInterpolated($"""
            SELECT * FROM orders WHERE "Id" = {outcome.OrderId} FOR UPDATE
            """).SingleAsync(ct);
        var action = envelope.Type switch
        {
            EventTypes.StockReserved => "stock-reserved",
            EventTypes.StockReservationFailed => "stock-rejected",
            EventTypes.PaymentApproved => "payment-approved",
            EventTypes.PaymentFailed => "payment-failed",
            EventTypes.StockReleased => "stock-released",
            _ => throw new InvalidOperationException("Unsupported order event.")
        };
        if (!order.Apply(action, outcome.Reason)) return;
        if (order.Status == OrderStatus.Confirmed)
            db.Enqueue(EventEnvelope.Create(EventTypes.OrderConfirmed, outcome, envelope));
        else if (order.Status == OrderStatus.Cancelled)
            db.Enqueue(EventEnvelope.Create(EventTypes.OrderCancelled,
                outcome with { Reason = order.FailureReason }, envelope));
    }
}
