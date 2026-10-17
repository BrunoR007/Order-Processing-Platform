using System.Diagnostics;
using System.Text.Json;

namespace OrderProcessing.Messaging;

public sealed record EventEnvelope(
    Guid MessageId, string Type, Guid CorrelationId, Guid? CausationId,
    DateTimeOffset OccurredAt, string? TraceParent, string? TraceState, JsonElement Data,
    int Attempt = 0, string? LastError = null)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static EventEnvelope Create<T>(string type, T data, EventEnvelope? parent = null,
        Guid? correlationId = null) => new(
            Guid.NewGuid(), type, parent?.CorrelationId ?? correlationId ?? Guid.NewGuid(),
            parent?.MessageId, DateTimeOffset.UtcNow,
            Activity.Current?.Id ?? parent?.TraceParent,
            Activity.Current?.TraceStateString ?? parent?.TraceState,
            JsonSerializer.SerializeToElement(data, Json));

    public T Read<T>() => Data.Deserialize<T>(Json)
        ?? throw new JsonException($"Missing payload for {Type}.");
}
