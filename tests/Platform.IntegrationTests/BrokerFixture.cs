using Microsoft.Extensions.Configuration;
using Testcontainers.RabbitMq;
using Xunit;

namespace OrderProcessing.Platform.IntegrationTests;

public sealed class BrokerFixture : IAsyncLifetime
{
    private readonly string password = Guid.NewGuid().ToString("N");
    private RabbitMqContainer? container;

    public async Task InitializeAsync()
    {
        container = new RabbitMqBuilder("rabbitmq:4.1-management").WithUsername("tests").WithPassword(password).Build();
        await container.StartAsync();
    }

    public IConfigurationRoot Configuration(int failures = 0, string rate = "0") => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["RabbitMQ:Host"] = container!.Hostname,
            ["RabbitMQ:Port"] = container.GetMappedPublicPort(5672).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["RabbitMQ:Username"] = "tests",
            ["RabbitMQ:Password"] = password,
            ["Payments:TransientFailuresBeforeSuccess"] = failures.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["Payments:FailureRate"] = rate
        }).Build();

    public async Task DisposeAsync()
    {
        if (container is not null) await container.DisposeAsync();
    }
}

[CollectionDefinition("messaging", DisableParallelization = true)]
public sealed class MessagingCollection : ICollectionFixture<BrokerFixture>, ICollectionFixture<PostgresFixture>;
