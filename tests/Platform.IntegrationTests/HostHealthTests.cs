using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Xunit;

namespace OrderProcessing.Platform.IntegrationTests;

public abstract class HostHealthTests<TProgram> where TProgram : class
{
    private static WebApplicationFactory<TProgram> Factory() => new WebApplicationFactory<TProgram>()
        .WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureServices(services => services.PostConfigure<HealthCheckServiceOptions>(options =>
            {
                foreach (var check in options.Registrations.Where(x => x.Name is "database" or "rabbitmq").ToArray())
                    options.Registrations.Remove(check);
            }));
        });

    [Theory]
    [InlineData("/health/live")]
    [InlineData("/health/ready")]
    public async Task Running_host_returns_healthy(string path)
    {
        await using var factory = Factory();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Theory]
    [InlineData("/health/ready", HttpStatusCode.ServiceUnavailable)]
    [InlineData("/health/live", HttpStatusCode.OK)]
    public async Task Dependency_failure_affects_readiness_but_not_liveness(
        string path, HttpStatusCode expected)
    {
        await using var factory = Factory()
            .WithWebHostBuilder(builder => builder.ConfigureServices(services =>
                services.AddHealthChecks().AddCheck("unavailable-dependency",
                    () => HealthCheckResult.Unhealthy("Simulated outage"), tags: ["ready"])));
        using var client = factory.CreateClient();

        using var response = await client.GetAsync(path);

        Assert.Equal(expected, response.StatusCode);
    }
}

public sealed class OrdersHostTests : HostHealthTests<Orders.Api.Program>;
public sealed class InventoryHostTests : HostHealthTests<Inventory.Service.Program>;
public sealed class PaymentsHostTests : HostHealthTests<Payments.Service.Program>;
public sealed class NotificationsHostTests : HostHealthTests<Notifications.Service.Program>;
