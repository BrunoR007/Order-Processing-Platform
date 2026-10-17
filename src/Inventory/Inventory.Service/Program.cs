using OrderProcessing.Messaging;

namespace OrderProcessing.Inventory.Service;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddMessaging<InventoryDb, InventoryEventHandler>("inventory");
        var app = builder.Build();
        app.MapServiceHealth();
        if (!app.Environment.IsEnvironment("Testing")) await app.InitializeDatabase<InventoryDb>();
        await app.RunAsync();
    }
}
