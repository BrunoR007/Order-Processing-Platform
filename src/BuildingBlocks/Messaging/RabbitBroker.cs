using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OrderProcessing.Contracts;
using RabbitMQ.Client;

namespace OrderProcessing.Messaging;

public sealed class RabbitBroker(IConfiguration configuration) : IAsyncDisposable
{
    public const string Exchange = "platform.events";
    public static readonly int[] RetryDelays = [1000, 5000, 15000];
    public static readonly IReadOnlyDictionary<string, string[]> Subscriptions =
        new Dictionary<string, string[]>
        {
            ["orders"] = [EventTypes.StockReserved, EventTypes.StockReservationFailed,
                EventTypes.PaymentApproved, EventTypes.PaymentFailed, EventTypes.StockReleased],
            ["inventory"] = [EventTypes.OrderCreated, EventTypes.PaymentFailed],
            ["payments"] = [EventTypes.StockReserved],
            ["notifications"] = [EventTypes.OrderConfirmed, EventTypes.OrderCancelled]
        };

    private readonly SemaphoreSlim gate = new(1, 1);
    private IConnection? connection;
    public bool IsConnected => connection?.IsOpen == true;

    public async Task<IChannel> OpenChannel(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (connection?.IsOpen != true)
            {
                if (connection is not null) await connection.DisposeAsync();
                var factory = new ConnectionFactory
                {
                    HostName = configuration["RabbitMQ:Host"] ?? "localhost",
                    Port = configuration.GetValue("RabbitMQ:Port", 5672),
                    UserName = configuration["RabbitMQ:Username"] ?? throw new InvalidOperationException("RabbitMQ username required."),
                    Password = configuration["RabbitMQ:Password"] ?? throw new InvalidOperationException("RabbitMQ password required."),
                    AutomaticRecoveryEnabled = false,
                    RequestedConnectionTimeout = TimeSpan.FromSeconds(5)
                };
                connection = await factory.CreateConnectionAsync(cancellationToken);
            }

            var channel = await connection.CreateChannelAsync(new CreateChannelOptions(
                publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), cancellationToken);
            try
            {
                await DeclareTopology(channel, cancellationToken);
                return channel;
            }
            catch
            {
                await channel.DisposeAsync();
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public static async Task DeclareTopology(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        foreach (var (service, events) in Subscriptions)
        {
            await channel.QueueDeclareAsync(service, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-queue-type"] = "quorum",
                    ["x-delivery-limit"] = 20,
                    ["x-dead-letter-exchange"] = "",
                    ["x-dead-letter-routing-key"] = $"{service}.dead",
                    ["x-dead-letter-strategy"] = "at-least-once",
                    ["x-overflow"] = "reject-publish"
                }, cancellationToken: ct);
            await channel.QueueDeclareAsync($"{service}.dead", durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?> { ["x-queue-type"] = "quorum", ["x-delivery-limit"] = -1 }, cancellationToken: ct);
            foreach (var type in events)
                await channel.QueueBindAsync(service, Exchange, type, cancellationToken: ct);
            for (var index = 0; index < RetryDelays.Length; index++)
                await channel.QueueDeclareAsync($"{service}.retry.{index + 1}", durable: true,
                    exclusive: false, autoDelete: false, arguments: new Dictionary<string, object?>
                    {
                        ["x-queue-type"] = "quorum",
                        ["x-dead-letter-strategy"] = "at-least-once",
                        ["x-overflow"] = "reject-publish",
                        ["x-message-ttl"] = RetryDelays[index],
                        ["x-dead-letter-exchange"] = "",
                        ["x-dead-letter-routing-key"] = service
                    }, cancellationToken: ct);
        }
    }

    public static async Task Publish(IChannel channel, EventEnvelope envelope, string? queue, CancellationToken ct)
    {
        ActivityContext.TryParse(envelope.TraceParent, envelope.TraceState, isRemote: false, out var parent);
        using var activity = InboxProcessor.Activities.StartActivity($"{envelope.Type} publish", ActivityKind.Producer, parent);
        activity?.SetTag("messaging.message.id", envelope.MessageId);
        activity?.SetTag("correlation.id", envelope.CorrelationId);
        activity?.SetTag("messaging.destination.name", queue ?? Exchange);
        envelope = envelope with { TraceParent = activity?.Id ?? envelope.TraceParent, TraceState = activity?.TraceStateString ?? envelope.TraceState };
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = envelope.MessageId.ToString(),
            CorrelationId = envelope.CorrelationId.ToString(),
            Type = envelope.Type,
            Headers = new Dictionary<string, object?>
            {
                ["traceparent"] = envelope.TraceParent ?? "",
                ["tracestate"] = envelope.TraceState ?? "",
                ["causation-id"] = envelope.CausationId?.ToString() ?? ""
            }
        };
        try
        {
            await channel.BasicPublishAsync(queue is null ? Exchange : "", queue ?? envelope.Type,
                mandatory: true, basicProperties: properties,
                body: JsonSerializer.SerializeToUtf8Bytes(envelope, EventEnvelope.Json), cancellationToken: timeout.Token);
        }
        catch (Exception exception)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.GetType().Name);
            throw;
        }
        MessageTelemetry.Published.Add(1, new KeyValuePair<string, object?>("event.type", envelope.Type));
    }

    public static async Task Quarantine(IChannel channel, BasicGetResult delivery, string consumer,
        string failureType, CancellationToken ct)
    {
        // Preserve the original bytes and AMQP identity even when JSON cannot be decoded.
        var properties = new BasicProperties(delivery.BasicProperties)
        {
            Persistent = true,
            Expiration = null,
            UserId = null,
            Headers = delivery.BasicProperties.Headers is { } headers
                ? new Dictionary<string, object?>(headers) : new Dictionary<string, object?>()
        };
        properties.Headers["failure-type"] = failureType;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await channel.BasicPublishAsync("", $"{consumer}.dead", mandatory: true,
            basicProperties: properties, body: delivery.Body, cancellationToken: timeout.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (connection is not null) await connection.DisposeAsync();
        gate.Dispose();
    }
}
