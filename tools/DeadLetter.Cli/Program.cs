using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using OrderProcessing.Messaging;

if (args.Length is < 2 or > 3 || args[0] is not ("inspect" or "replay") ||
    !RabbitBroker.Subscriptions.ContainsKey(args[1]))
{
    Console.Error.WriteLine("Usage: inspect|replay orders|inventory|payments|notifications [count:1..100]");
    return 2;
}
var count = 1;
if (args.Length == 3 && (!int.TryParse(args[2], out count) || count is < 1 or > 100)) return 2;
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
await using var broker = new RabbitBroker(config);
await using var channel = await broker.OpenChannel(cancellation.Token);
var deliveries = new List<ulong>();
for (var index = 0; index < count; index++)
{
    var delivery = await channel.BasicGetAsync($"{args[1]}.dead", false, cancellation.Token);
    if (delivery is null) break;
    deliveries.Add(delivery.DeliveryTag);
    if (args[0] == "inspect")
    {
        Console.WriteLine(Encoding.UTF8.GetString(delivery.Body.Span));
        continue;
    }
    var envelope = JsonSerializer.Deserialize<EventEnvelope>(delivery.Body.Span, EventEnvelope.Json)
        ?? throw new JsonException("Cannot replay malformed envelope. It remains on the dead-letter queue.");
    var replay = envelope with { Attempt = 0, LastError = null };
    replay.ValidateFor(args[1]);
    // Replay only this consumer, keeping its dedup identity and original trace context.
    await RabbitBroker.Publish(channel, replay, args[1], cancellation.Token);
    await channel.BasicAckAsync(delivery.DeliveryTag, false, cancellation.Token);
    Console.WriteLine($"Replayed {envelope.MessageId} to {args[1]}.");
}
if (args[0] == "inspect")
    foreach (var tag in deliveries) await channel.BasicNackAsync(tag, false, true, cancellation.Token);
Console.WriteLine($"{deliveries.Count} message(s) {args[0]}ed.");
return 0;
