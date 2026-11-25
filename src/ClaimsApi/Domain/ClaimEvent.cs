using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaimsApi.Domain;

/// <summary>
/// One thing that happened to a claim. The same row serves two purposes:
/// it is the claim's timeline, and it is the outbox. It is saved in the same transaction
/// as the claim change, and the outbox publisher later sends it to Kafka and sets PublishedAt.
/// </summary>
public sealed class ClaimEvent
{
    static readonly JsonSerializerOptions PayloadJson = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public Guid Id { get; private set; } // also the event id consumers de-duplicate on
    public Guid ClaimId { get; private set; }
    public ClaimEventType Type { get; private set; }
    public Guid ActorId { get; private set; }
    public DateTimeOffset OccurredAt { get; private set; }
    public string Payload { get; private set; } = "{}";
    public DateTimeOffset? PublishedAt { get; private set; }

    private ClaimEvent() { } // for EF Core

    internal static ClaimEvent Create(Guid claimId, ClaimEventType type, Guid actorId, object data, DateTimeOffset now) => new()
    {
        Id = Guid.NewGuid(),
        ClaimId = claimId,
        Type = type,
        ActorId = actorId,
        OccurredAt = now,
        Payload = JsonSerializer.Serialize(data, PayloadJson),
    };
}
