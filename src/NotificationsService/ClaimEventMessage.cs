using System.Text.Json;

namespace NotificationsService;

/// <summary>
/// This service's own view of a claim-events message. The JSON shape is the contract (DESIGN.md §8).
/// There is deliberately no shared library with the Claims API, and only the fields we need are read.
/// </summary>
public record ClaimEventMessage(
    Guid EventId, string Type, Guid ClaimId, string ClaimNumber, Guid ClaimantId, DateTimeOffset OccurredAt, JsonElement Data)
{
    /// <summary>
    /// Has everything needed to store a notification safely. An invalid message is skipped, not retried:
    /// reading it again would fail the same way and stall every event behind it.
    /// </summary>
    public bool IsValid =>
        EventId != Guid.Empty && ClaimId != Guid.Empty && ClaimantId != Guid.Empty
        && !string.IsNullOrWhiteSpace(Type)
        && !string.IsNullOrWhiteSpace(ClaimNumber) && ClaimNumber.Length <= 20;
}
