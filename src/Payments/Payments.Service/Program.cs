using OrderProcessing.Messaging;

namespace OrderProcessing.Payments.Service;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddMessaging<PaymentsDb, PaymentEventHandler>("payments");
        var app = builder.Build();
        app.MapServiceHealth();
        if (!app.Environment.IsEnvironment("Testing")) await app.InitializeDatabase<PaymentsDb>();
        await app.RunAsync();
    }
}
