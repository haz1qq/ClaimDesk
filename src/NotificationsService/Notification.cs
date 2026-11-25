namespace NotificationsService;

/// <summary>A message shown to a claimant (and, in a real system, emailed to them).</summary>
public sealed class Notification
{
    public Guid Id { get; set; }
    public Guid RecipientId { get; set; }
    public Guid ClaimId { get; set; }
    public string ClaimNumber { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Remembers which events were handled, so a redelivered event is ignored (idempotency).</summary>
public sealed class ProcessedEvent
{
    public Guid EventId { get; set; }
    public DateTimeOffset ProcessedAt { get; set; }
}

public record NotificationDto(Guid Id, Guid ClaimId, string ClaimNumber, string Message, DateTimeOffset CreatedAt);
