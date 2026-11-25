namespace ClaimsApi.Domain;

/// <summary>A question an officer asks the claimant. Open until the claimant answers.</summary>
public sealed class InformationRequest
{
    public Guid Id { get; private set; }
    public Guid ClaimId { get; private set; }
    public string Question { get; private set; } = "";
    public string? Response { get; private set; }
    public Guid RequestedBy { get; private set; }
    public DateTimeOffset RequestedAt { get; private set; }
    public DateTimeOffset? RespondedAt { get; private set; }

    public bool IsOpen => Response is null;

    private InformationRequest() { } // for EF Core

    internal InformationRequest(Guid claimId, Guid requestedBy, string question, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        ClaimId = claimId;
        RequestedBy = requestedBy;
        Question = question;
        RequestedAt = now;
    }

    internal void Answer(string response, DateTimeOffset now)
    {
        Response = response;
        RespondedAt = now;
    }
}
