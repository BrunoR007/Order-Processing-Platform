using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace OrderProcessing.Messaging;

public sealed class MessageConsumer<TDb, THandler>(IServiceScopeFactory scopes,
    RabbitBroker broker, ServiceIdentity identity, ILogger<MessageConsumer<TDb, THandler>> logger)
    : BackgroundService where TDb : MessageDbContext where THandler : class, IEventHandler
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var channel = await broker.OpenChannel(stoppingToken);
                while (channel.IsOpen && !stoppingToken.IsCancellationRequested)
                {
                    var delivery = await channel.BasicGetAsync(identity.Name, autoAck: false, stoppingToken);
                    if (delivery is null)
                    {
                        await Task.Delay(100, stoppingToken);
                        continue;
                    }
                    EventEnvelope? envelope = null;
                    try
                    {
                        envelope = JsonSerializer.Deserialize<EventEnvelope>(delivery.Body.Span, EventEnvelope.Json)
                            ?? throw new JsonException("Missing envelope.");
                        if (envelope.MessageId == Guid.Empty || envelope.CorrelationId == Guid.Empty ||
                            envelope.Attempt < 0 || !RabbitBroker.Subscriptions[identity.Name].Contains(envelope.Type))
                            throw new JsonException("Invalid or unsupported event envelope.");
                        logger.LogInformation("Processing {EventType}, message {MessageId}, correlation {CorrelationId}, attempt {Attempt}",
                            envelope.Type, envelope.MessageId, envelope.CorrelationId, envelope.Attempt);
                        await using var scope = scopes.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<TDb>();
                        var handler = scope.ServiceProvider.GetRequiredService<THandler>();
                        await InboxProcessor.Process(db, identity.Name, envelope, handler, stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception exception)
                    {
                        logger.LogWarning(exception, "Consumer {Consumer} failed message {MessageId}, attempt {Attempt}",
                            identity.Name, envelope?.MessageId, envelope?.Attempt);
                        // A malformed payload still goes to the DLQ as the original bytes.
                        if (envelope is null)
                        {
                            await channel.BasicPublishAsync("", $"{identity.Name}.dead", mandatory: true,
                                basicProperties: new RabbitMQ.Client.BasicProperties { Persistent = true },
                                body: delivery.Body, cancellationToken: stoppingToken);
                            MessageTelemetry.DeadLetters.Add(1);
                        }
                        else
                        {
                            var attempt = envelope.Attempt + 1;
                            var target = exception is not JsonException && attempt <= RabbitBroker.RetryDelays.Length
                                ? $"{identity.Name}.retry.{attempt}" : $"{identity.Name}.dead";
                            await RabbitBroker.Publish(channel, envelope with
                            {
                                Attempt = attempt,
                                LastError = exception.GetType().Name
                            }, target, stoppingToken);
                            if (target.EndsWith(".dead", StringComparison.Ordinal)) MessageTelemetry.DeadLetters.Add(1);
                            else MessageTelemetry.Retries.Add(1);
                        }
                    }
                    // On failed forwarding/commit this is never reached: closing the channel requeues delivery.
                    await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Consumer disconnected; reconnecting");
                await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            }
        }
    }
}

public sealed record ServiceIdentity(string Name);
