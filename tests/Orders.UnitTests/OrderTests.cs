using OrderProcessing.Orders.Application;
using OrderProcessing.Orders.Domain;
using Xunit;

namespace OrderProcessing.Orders.UnitTests;

public sealed class OrderTests
{
    [Fact]
    public void Payment_failure_does_not_cancel_before_stock_release()
    {
        var order = new Order();
        order.Apply("stock-reserved", null);
        order.Apply("payment-failed", "Declined");
        Assert.Equal(OrderStatus.Compensating, order.Status);
        order.Apply("stock-released", "Declined");
        Assert.Equal(OrderStatus.Cancelled, order.Status);
    }

    [Theory]
    [InlineData("payment-approved", OrderStatus.Confirmed)]
    [InlineData("stock-released", OrderStatus.Cancelled)]
    public void Authoritative_outcomes_tolerate_delayed_stock_reserved(string outcome, OrderStatus expected)
    {
        var order = new Order();
        Assert.True(order.Apply(outcome, null));
        Assert.False(order.Apply("stock-reserved", null));
        Assert.Equal(expected, order.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Invalid_quantities_are_rejected(int quantity) =>
        Assert.Throws<OrderValidationException>(() => SampleCatalog.Validate(new([new("BOOK-DOTNET", quantity)])));

    [Fact]
    public void Duplicate_lines_are_canonicalized_before_hashing()
    {
        var lines = SampleCatalog.Validate(new([new("MUG-EVENTS", 1), new("BOOK-DOTNET", 1), new("MUG-EVENTS", 2)]));
        Assert.Equal([new CreateOrderLine("BOOK-DOTNET", 1), new("MUG-EVENTS", 3)], lines);
    }
}
