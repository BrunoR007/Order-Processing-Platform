namespace OrderProcessing.Orders.Domain;

public enum OrderStatus { PendingInventory, AwaitingPayment, Compensating, Confirmed, Cancelled }

public sealed class Order
{
    public Guid Id { get; set; }
    public Guid CorrelationId { get; set; }
    public string RequestKey { get; set; } = "";
    public string RequestHash { get; set; } = "";
    public string LinesJson { get; set; } = "[]";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public OrderStatus Status { get; set; }
    public string? FailureReason { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    public bool Apply(string outcome, string? reason)
    {
        if (Status is OrderStatus.Confirmed or OrderStatus.Cancelled) return false;
        switch (outcome)
        {
            case "stock-reserved" when Status == OrderStatus.PendingInventory:
                Status = OrderStatus.AwaitingPayment;
                break;
            case "payment-approved":
                if (Status == OrderStatus.Compensating) throw new InvalidOperationException("Conflicting payment outcomes.");
                Status = OrderStatus.Confirmed;
                break;
            case "payment-failed":
                Status = OrderStatus.Compensating;
                FailureReason = reason;
                break;
            case "stock-rejected":
            case "stock-released":
                Status = OrderStatus.Cancelled;
                FailureReason = reason ?? FailureReason;
                break;
            default: return false;
        }
        return true;
    }
}
