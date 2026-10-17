using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderProcessing.Contracts;
using OrderProcessing.Messaging;
using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Domain;

namespace OrderProcessing.Orders.Infrastructure;

public sealed class OrderStore(OrdersDb db) : IOrderStore
{
    public async Task<OrderResult> Create(CreateOrderRequest request, string requestKey, Guid correlationId, CancellationToken ct)
    {
        var lines = SampleCatalog.Validate(request);
        var json = JsonSerializer.Serialize(lines, EventEnvelope.Json);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var existing = await db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.RequestKey == requestKey, ct);
        if (existing is not null) return Reuse(existing, hash);
        var order = new Order
        {
            Id = Guid.NewGuid(),
            CorrelationId = correlationId,
            RequestKey = requestKey,
            RequestHash = hash,
            LinesJson = json,
            Amount = lines.Sum(x => SampleCatalog.Prices[x.Sku] * x.Quantity),
            CreatedAt = DateTimeOffset.UtcNow,
            Status = OrderStatus.PendingInventory
        };
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        db.Orders.Add(order);
        db.Enqueue(EventEnvelope.Create(EventTypes.OrderCreated,
            new OrderCreated(order.Id, lines.Select(x => new OrderLine(x.Sku, x.Quantity)).ToArray(), order.Amount, order.Currency),
            correlationId: correlationId));
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new(order, true);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: "23505" })
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            existing = await db.Orders.AsNoTracking().SingleAsync(x => x.RequestKey == requestKey, ct);
            return Reuse(existing, hash);
        }
    }

    private static OrderResult Reuse(Order order, string hash) => order.RequestHash == hash
        ? new(order, false) : throw new IdempotencyConflictException();

    public Task<Order?> Find(Guid id, CancellationToken ct) => db.Orders.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
}
