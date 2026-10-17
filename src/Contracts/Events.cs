namespace OrderProcessing.Contracts;

public static class EventTypes
{
    public const string OrderCreated = "order.created.v1";
    public const string StockReserved = "stock.reserved.v1";
    public const string StockReservationFailed = "stock.reservation-failed.v1";
    public const string PaymentApproved = "payment.approved.v1";
    public const string PaymentFailed = "payment.failed.v1";
    public const string StockReleased = "stock.released.v1";
    public const string OrderConfirmed = "order.confirmed.v1";
    public const string OrderCancelled = "order.cancelled.v1";
}

public sealed record OrderLine(string Sku, int Quantity);
public sealed record OrderCreated(Guid OrderId, OrderLine[] Lines, decimal Amount, string Currency);
public sealed record StockReserved(Guid OrderId, decimal Amount, string Currency);
public sealed record OrderOutcome(Guid OrderId, string? Reason = null);
