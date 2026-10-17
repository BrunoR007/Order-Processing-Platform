using System.Diagnostics.Metrics;

namespace OrderProcessing.Messaging;

public static class MessageTelemetry
{
    public static readonly Meter Meter = new("OrderProcessing.Messaging");
    public static readonly Counter<long> Published = Meter.CreateCounter<long>("messages.published");
    public static readonly Counter<long> Processed = Meter.CreateCounter<long>("messages.processed");
    public static readonly Counter<long> Duplicates = Meter.CreateCounter<long>("messages.duplicates");
    public static readonly Counter<long> Retries = Meter.CreateCounter<long>("messages.retries");
    public static readonly Counter<long> DeadLetters = Meter.CreateCounter<long>("messages.deadletters");
    public static readonly Histogram<double> OutboxLatency = Meter.CreateHistogram<double>("outbox.dispatch.latency", "s");
    public static long PendingOutbox;
    private static readonly ObservableGauge<long> Backlog = Meter.CreateObservableGauge("outbox.pending", () => Interlocked.Read(ref PendingOutbox));
}
