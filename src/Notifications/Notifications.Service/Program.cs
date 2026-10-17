using OrderProcessing.Messaging;

namespace OrderProcessing.Notifications.Service;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddMessaging<NotificationsDb, NotificationEventHandler>("notifications");
        var app = builder.Build();
        app.MapServiceHealth();
        if (!app.Environment.IsEnvironment("Testing")) await app.InitializeDatabase<NotificationsDb>();
        await app.RunAsync();
    }
}
