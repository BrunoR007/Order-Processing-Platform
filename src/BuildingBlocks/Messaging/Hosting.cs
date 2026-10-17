using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Npgsql;
using OrderProcessing.Observability;

namespace OrderProcessing.Messaging;

public static class Hosting
{
    public static void AddMessaging<TDb, THandler>(this WebApplicationBuilder builder, string service)
        where TDb : MessageDbContext where THandler : class, IEventHandler
    {
        builder.Logging.AddJsonConsole();
        builder.AddPlatformTelemetry(service);
        builder.Services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromSeconds(30));
        builder.Services.AddSingleton(new ServiceIdentity(service));
        builder.Services.AddSingleton<RabbitBroker>();
        builder.Services.AddScoped<THandler>();
        builder.Services.AddDbContext<TDb>(options => options.UseNpgsql(ConnectionString(builder.Configuration)));
        builder.Services.AddHealthChecks().AddCheck<DatabaseHealth<TDb>>("database", tags: ["ready"])
            .AddCheck<BrokerHealth>("rabbitmq", tags: ["ready"]);
        if (!builder.Environment.IsEnvironment("Testing"))
        {
            builder.Services.AddHostedService<OutboxRelay<TDb>>();
            builder.Services.AddHostedService<MessageConsumer<TDb, THandler>>();
        }
    }

    public static string ConnectionString(IConfiguration config) => config["Database:ConnectionString"] ?? new NpgsqlConnectionStringBuilder
    {
        Host = config["Database:Host"] ?? "localhost",
        Port = config.GetValue("Database:Port", 5432),
        Database = config["Database:Name"] ?? "unconfigured",
        Username = config["Database:Username"] ?? "unconfigured",
        Password = config["Database:Password"] ?? "",
        Timeout = 5,
        CommandTimeout = 15,
        IncludeErrorDetail = false
    }.ConnectionString;

    public static async Task InitializeDatabase<TDb>(this WebApplication app, CancellationToken ct = default)
        where TDb : MessageDbContext
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDb>();
        await db.Database.MigrateAsync(ct);
    }

    public static void MapServiceHealth(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.Use(async (context, next) =>
        {
            if (context.Request.Path == "/health/ready" && app.Lifetime.ApplicationStopping.IsCancellationRequested)
            {
                context.Response.StatusCode = 503;
                return;
            }
            await next(context);
        });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
    }
}

public sealed class DatabaseHealth<TDb>(IServiceScopeFactory scopes) : IHealthCheck where TDb : MessageDbContext
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDb>();
        return await db.Database.CanConnectAsync(cancellationToken)
            ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Database unavailable");
    }
}

public sealed class BrokerHealth(RabbitBroker broker) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(broker.IsConnected ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("RabbitMQ disconnected"));
}
