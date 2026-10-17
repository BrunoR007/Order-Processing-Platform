using OrderProcessing.Orders.Domain;

namespace OrderProcessing.Orders.Application;

public sealed record CreateOrderLine(string Sku, int Quantity);
public sealed record CreateOrderRequest(CreateOrderLine[]? Lines);
public sealed record OrderResult(Order Order, bool Created);

public interface IOrderStore
{
    Task<OrderResult> Create(CreateOrderRequest request, string requestKey, Guid correlationId, CancellationToken ct);
    Task<Order?> Find(Guid id, CancellationToken ct);
}

public sealed class OrderValidationException(string message) : Exception(message);
public sealed class IdempotencyConflictException : Exception;

public static class SampleCatalog
{
    // Fictional prices in USD. Clients choose SKUs/quantities, never the payable amount.
    public static readonly IReadOnlyDictionary<string, decimal> Prices = new Dictionary<string, decimal>
    {
        ["BOOK-DOTNET"] = 29.90m,
        ["MUG-EVENTS"] = 12.50m,
        ["SHIRT-ASYNC"] = 24.00m
    };

    public static CreateOrderLine[] Validate(CreateOrderRequest request)
    {
        if (request.Lines is not { Length: > 0 and <= 20 } ||
            request.Lines.Any(x => x is null || string.IsNullOrWhiteSpace(x.Sku) ||
                !Prices.ContainsKey(x.Sku) || x.Quantity is < 1 or > 100))
            throw new OrderValidationException("Supply 1–20 lines with known sample SKUs and quantities from 1 to 100.");
        var lines = request.Lines.GroupBy(x => x.Sku).OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(group => new CreateOrderLine(group.Key, group.Sum(x => x.Quantity))).ToArray();
        if (lines.Any(x => x.Quantity > 100))
            throw new OrderValidationException("The total quantity per SKU must not exceed 100.");
        return lines;
    }
}
