using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace OrderProcessing.Notifications.Service;

public partial class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Logging.AddJsonConsole();
        builder.Services.Configure<HostOptions>(options =>
            options.ShutdownTimeout = TimeSpan.FromSeconds(30));
        builder.Services.AddHealthChecks()
            .AddCheck("host", () => HealthCheckResult.Healthy(), tags: ["ready"]);

        var app = builder.Build();

        // The skeleton has no dependency clients yet. Add their checks with those integrations.
        // During shutdown, readiness must fail while liveness still reflects a running process.
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = _ => false
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready")
        });
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/health/ready" &&
                app.Lifetime.ApplicationStopping.IsCancellationRequested)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            await next(context);
        });

        await app.RunAsync();
    }
}
