using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OrderProcessing.Contracts;
using OrderProcessing.Messaging;

namespace OrderProcessing.Payments.Service;

public sealed class PaymentEventHandler(PaymentsDb db, IConfiguration configuration) : IEventHandler
{
    public async Task Handle(EventEnvelope envelope, CancellationToken ct)
    {
        if (envelope.Type != EventTypes.StockReserved) throw new InvalidOperationException("Unsupported payment event.");
        var order = envelope.Read<StockReserved>();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({order.OrderId.ToString()}, 0))", ct);
        if (await db.Payments.AnyAsync(x => x.OrderId == order.OrderId, ct)) return;
        var failures = configuration.GetValue("Payments:TransientFailuresBeforeSuccess", 0);
        var rate = configuration.GetValue("Payments:FailureRate", 0.15);
        if (failures is < 0 or > 10 || rate is < 0 or > 1 || double.IsNaN(rate))
            throw new InvalidOperationException("Payment simulation configuration is invalid.");
        if (envelope.Attempt < failures) throw new IOException("Simulated temporary payment provider outage.");
        if (order.Amount <= 0 || order.Currency != "USD") throw new InvalidOperationException("Invalid payment amount or currency.");
        // Stable across retries, processes, and deployments; no external charge is made.
        var sample = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(SHA256.HashData(order.OrderId.ToByteArray()))
            / ((double)uint.MaxValue + 1);
        var approved = sample >= rate;
        db.Payments.Add(new Payment { OrderId = order.OrderId, Amount = order.Amount, Currency = order.Currency, Approved = approved });
        db.Enqueue(EventEnvelope.Create(approved ? EventTypes.PaymentApproved : EventTypes.PaymentFailed,
            new OrderOutcome(order.OrderId, approved ? null : "Simulated payment declined"), envelope));
    }
}
