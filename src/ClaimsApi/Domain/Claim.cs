namespace ClaimsApi.Domain;

/// <summary>
/// The claim aggregate. Every change goes through one of its methods, which enforce the
/// state machine in DESIGN.md §6 and append exactly one <see cref="ClaimEvent"/>.
/// Checks run in this order: right person (403), then right state (409), then valid input (400).
/// </summary>
public sealed class Claim
{
    public Guid Id { get; private set; }
    public int Number { get; private set; } // assigned by the database
    public string DisplayNumber => FormatNumber(Number);
    public static string FormatNumber(int number) => $"CLM-{number:D6}";
    public Guid ClaimantId { get; private set; }
    public Market Market { get; private set; }
    public string Currency { get; private set; } = "";
    public ClaimType Type { get; private set; }
    public DateOnly IncidentDate { get; private set; }
    public string Description { get; private set; } = "";
    public decimal ClaimedAmount { get; private set; }

    /// <summary>The insurer's current estimate of what this claim will cost. Open reserves = liability exposure.</summary>
    public decimal Reserve { get; private set; }

    public decimal? SettlementAmount { get; private set; }
    public string? RejectionReason { get; private set; }
    public ClaimStatus Status { get; private set; }
    public Guid? AssignedOfficerId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }

    /// <summary>Concurrency token (Postgres xmin). Two officers saving the same claim: the second gets a conflict.</summary>
    public uint Version { get; private set; }

    public List<InformationRequest> InformationRequests { get; private set; } = [];
    public List<ClaimEvent> Events { get; private set; } = [];

    public bool IsOpen => Status is ClaimStatus.Submitted or ClaimStatus.UnderReview or ClaimStatus.AwaitingInformation;

    private Claim() { } // for EF Core

    public static Claim Submit(
        Guid claimantId, Market market, ClaimType type, DateOnly incidentDate,
        string description, decimal claimedAmount, DateTimeOffset now)
    {
        if (claimedAmount <= 0)
            throw ClaimRuleException.Validation("Claimed amount must be greater than zero.");
        if (incidentDate > DateOnly.FromDateTime(now.UtcDateTime))
            throw ClaimRuleException.Validation("Incident date cannot be in the future.");
        if (string.IsNullOrWhiteSpace(description))
            throw ClaimRuleException.Validation("Description is required.");

        var claim = new Claim
        {
            Id = Guid.NewGuid(),
            ClaimantId = claimantId,
            Market = market,
            Currency = Markets.CurrencyFor(market),
            Type = type,
            IncidentDate = incidentDate,
            Description = description.Trim(),
            ClaimedAmount = claimedAmount,
            Reserve = claimedAmount, // initial reserve = what the claimant asked for
            Status = ClaimStatus.Submitted,
            CreatedAt = now,
        };
        claim.Record(ClaimEventType.ClaimSubmitted, claimantId,
            new { type, market, currency = claim.Currency, claimedAmount }, now);
        return claim;
    }

    public void Assign(Guid officerId, DateTimeOffset now)
    {
        RequireStatus(ClaimStatus.Submitted, "Only a newly submitted claim can be picked up.");

        AssignedOfficerId = officerId;
        Status = ClaimStatus.UnderReview;
        Record(ClaimEventType.ClaimAssigned, officerId, new { officerId }, now);
    }

    public InformationRequest RequestInformation(Guid officerId, string question, DateTimeOffset now)
    {
        RequireAssignedOfficer(officerId);
        RequireStatus(ClaimStatus.UnderReview, "Information can only be requested while the claim is under review.");
        if (string.IsNullOrWhiteSpace(question))
            throw ClaimRuleException.Validation("A question is required.");

        var request = new InformationRequest(Id, officerId, question.Trim(), now);
        InformationRequests.Add(request);
        Status = ClaimStatus.AwaitingInformation;
        Record(ClaimEventType.InformationRequested, officerId, new { requestId = request.Id, question = request.Question }, now);
        return request;
    }

    public void RespondToInformationRequest(Guid claimantId, Guid requestId, string response, DateTimeOffset now)
    {
        if (claimantId != ClaimantId)
            throw ClaimRuleException.Forbidden("Only the claimant can answer this request.");
        RequireStatus(ClaimStatus.AwaitingInformation, "There is no open information request on this claim.");
        var request = InformationRequests.SingleOrDefault(r => r.Id == requestId && r.IsOpen)
            ?? throw ClaimRuleException.Conflict("That information request is not open.");
        if (string.IsNullOrWhiteSpace(response))
            throw ClaimRuleException.Validation("A response is required.");

        request.Answer(response.Trim(), now);
        Status = ClaimStatus.UnderReview;
        Record(ClaimEventType.InformationProvided, claimantId, new { requestId }, now);
    }

    public void AdjustReserve(Guid officerId, decimal newReserve, DateTimeOffset now)
    {
        RequireAssignedOfficer(officerId);
        if (Status is not (ClaimStatus.UnderReview or ClaimStatus.AwaitingInformation))
            throw ClaimRuleException.Conflict("The reserve can only be changed while the claim is open.");
        if (newReserve < 0)
            throw ClaimRuleException.Validation("Reserve cannot be negative.");

        var previous = Reserve;
        Reserve = newReserve;
        Record(ClaimEventType.ReserveAdjusted, officerId, new { previous, current = newReserve, currency = Currency }, now);
    }

    public void Settle(Guid officerId, decimal amount, DateTimeOffset now)
    {
        RequireAssignedOfficer(officerId);
        RequireStatus(ClaimStatus.UnderReview, "Only a claim under review can be settled.");
        if (amount <= 0)
            throw ClaimRuleException.Validation("Settlement amount must be greater than zero.");

        SettlementAmount = amount;
        Close(ClaimStatus.Settled, now);
        Record(ClaimEventType.ClaimSettled, officerId, new { amount, currency = Currency }, now);
    }

    public void Reject(Guid officerId, string reason, DateTimeOffset now)
    {
        RequireAssignedOfficer(officerId);
        RequireStatus(ClaimStatus.UnderReview, "Only a claim under review can be rejected.");
        if (string.IsNullOrWhiteSpace(reason))
            throw ClaimRuleException.Validation("A rejection reason is required.");

        RejectionReason = reason.Trim();
        Close(ClaimStatus.Rejected, now);
        Record(ClaimEventType.ClaimRejected, officerId, new { reason = RejectionReason }, now);
    }

    void RequireAssignedOfficer(Guid officerId)
    {
        // Nobody has picked it up yet: that's the claim's state, not the caller's identity.
        if (AssignedOfficerId is null)
            throw ClaimRuleException.Conflict("Pick up the claim before acting on it.");
        if (AssignedOfficerId != officerId)
            throw ClaimRuleException.Forbidden("Only the assigned officer can act on this claim.");
    }

    void RequireStatus(ClaimStatus expected, string message)
    {
        if (Status != expected)
            throw ClaimRuleException.Conflict(message);
    }

    void Close(ClaimStatus finalStatus, DateTimeOffset now)
    {
        Status = finalStatus;
        Reserve = 0; // a closed claim carries no liability
        DecidedAt = now;
    }

    void Record(ClaimEventType type, Guid actorId, object data, DateTimeOffset now)
    {
        UpdatedAt = now;
        Events.Add(ClaimEvent.Create(Id, type, actorId, data, now));
    }
}
