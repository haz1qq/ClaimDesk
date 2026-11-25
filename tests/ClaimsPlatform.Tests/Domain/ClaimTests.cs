using ClaimsApi.Domain;

namespace ClaimsPlatform.Tests.Domain;

public class ClaimTests
{
    static readonly DateTimeOffset Now = new(2026, 11, 26, 0, 0, 0, TimeSpan.Zero);
    static readonly Guid ClaimantA = Guid.Parse("00000000-0000-0000-0000-00000000c001");
    static readonly Guid ClaimantB = Guid.Parse("00000000-0000-0000-0000-00000000c002");
    static readonly Guid Officer1 = Guid.Parse("00000000-0000-0000-0000-00000000f001");
    static readonly Guid Officer2 = Guid.Parse("00000000-0000-0000-0000-00000000f002");

    static Claim NewClaim() =>
        Claim.Submit(ClaimantA, Market.MY, ClaimType.Motor, new DateOnly(2026, 11, 20), "Rear-ended at lights", 5000m, Now);

    static Claim UnderReview()
    {
        var claim = NewClaim();
        claim.Assign(Officer1, Now);
        return claim;
    }

    static (Claim Claim, InformationRequest Request) Awaiting()
    {
        var claim = UnderReview();
        var request = claim.RequestInformation(Officer1, "Police report?", Now);
        return (claim, request);
    }

    static void AssertViolation(RuleViolation expected, Action act)
    {
        var ex = Assert.Throws<ClaimRuleException>(act);
        Assert.Equal(expected, ex.Kind);
    }

    // ---- Submit ----

    [Fact]
    public void Submit_sets_initial_state()
    {
        var claim = NewClaim();

        Assert.Equal(ClaimStatus.Submitted, claim.Status);
        Assert.Equal("MYR", claim.Currency);
        Assert.Equal(5000m, claim.Reserve);
        Assert.Null(claim.AssignedOfficerId);
        Assert.Single(claim.Events);
        Assert.Equal(ClaimEventType.ClaimSubmitted, claim.Events.Last().Type);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Submit_rejects_non_positive_amount(decimal amount) =>
        AssertViolation(RuleViolation.Validation, () =>
            Claim.Submit(ClaimantA, Market.MY, ClaimType.Motor, new DateOnly(2026, 11, 20), "Rear-ended at lights", amount, Now));

    [Fact]
    public void Submit_rejects_future_incident_date() =>
        AssertViolation(RuleViolation.Validation, () =>
            Claim.Submit(ClaimantA, Market.MY, ClaimType.Motor, new DateOnly(2026, 11, 27), "Rear-ended at lights", 5000m, Now));

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public void Submit_rejects_blank_description(string description) =>
        AssertViolation(RuleViolation.Validation, () =>
            Claim.Submit(ClaimantA, Market.MY, ClaimType.Motor, new DateOnly(2026, 11, 20), description, 5000m, Now));

    // ---- Assign ----

    [Fact]
    public void Assign_moves_to_under_review()
    {
        var claim = NewClaim();

        claim.Assign(Officer1, Now);

        Assert.Equal(ClaimStatus.UnderReview, claim.Status);
        Assert.Equal(Officer1, claim.AssignedOfficerId);
        Assert.Equal(ClaimEventType.ClaimAssigned, claim.Events.Last().Type);
    }

    [Fact]
    public void Assign_twice_is_conflict() =>
        AssertViolation(RuleViolation.Conflict, () => UnderReview().Assign(Officer2, Now));

    // ---- Request information ----

    [Fact]
    public void Request_info_moves_to_awaiting()
    {
        var claim = UnderReview();

        var request = claim.RequestInformation(Officer1, "Police report?", Now);

        Assert.Equal(ClaimStatus.AwaitingInformation, claim.Status);
        var only = Assert.Single(claim.InformationRequests);
        Assert.Same(request, only);
        Assert.Equal("Police report?", only.Question);
        Assert.Null(only.Response);
        Assert.Equal(ClaimEventType.InformationRequested, claim.Events.Last().Type);
    }

    [Fact]
    public void Request_info_by_other_officer_is_forbidden() =>
        AssertViolation(RuleViolation.Forbidden, () => UnderReview().RequestInformation(Officer2, "Police report?", Now));

    [Fact]
    public void Request_info_while_awaiting_is_conflict()
    {
        var (claim, _) = Awaiting();
        AssertViolation(RuleViolation.Conflict, () => claim.RequestInformation(Officer1, "And photos?", Now));
    }

    [Fact]
    public void Request_info_blank_question_is_validation() =>
        AssertViolation(RuleViolation.Validation, () => UnderReview().RequestInformation(Officer1, " ", Now));

    // ---- Respond ----

    [Fact]
    public void Respond_returns_to_under_review()
    {
        var (claim, request) = Awaiting();

        claim.RespondToInformationRequest(ClaimantA, request.Id, "Attached ref 123", Now);

        Assert.Equal(ClaimStatus.UnderReview, claim.Status);
        Assert.Equal("Attached ref 123", request.Response);
        Assert.Equal(Now, request.RespondedAt);
        Assert.Equal(ClaimEventType.InformationProvided, claim.Events.Last().Type);
    }

    [Fact]
    public void Respond_by_other_claimant_is_forbidden()
    {
        var (claim, request) = Awaiting();
        AssertViolation(RuleViolation.Forbidden, () => claim.RespondToInformationRequest(ClaimantB, request.Id, "Hi", Now));
    }

    [Fact]
    public void Respond_to_wrong_request_is_conflict()
    {
        var (claim, _) = Awaiting();
        AssertViolation(RuleViolation.Conflict, () => claim.RespondToInformationRequest(ClaimantA, Guid.NewGuid(), "Hi", Now));
    }

    [Fact]
    public void Respond_when_not_awaiting_is_conflict() =>
        AssertViolation(RuleViolation.Conflict, () => UnderReview().RespondToInformationRequest(ClaimantA, Guid.NewGuid(), "Hi", Now));

    [Fact]
    public void Respond_blank_is_validation()
    {
        var (claim, request) = Awaiting();
        AssertViolation(RuleViolation.Validation, () => claim.RespondToInformationRequest(ClaimantA, request.Id, " ", Now));
    }

    // ---- Reserve ----

    [Fact]
    public void Adjust_reserve_allowed_while_under_review()
    {
        var claim = UnderReview();

        claim.AdjustReserve(Officer1, 7500m, Now);

        Assert.Equal(7500m, claim.Reserve);
        Assert.Equal(ClaimEventType.ReserveAdjusted, claim.Events.Last().Type);
    }

    [Fact]
    public void Adjust_reserve_allowed_while_awaiting_information()
    {
        var (claim, _) = Awaiting();

        claim.AdjustReserve(Officer1, 7500m, Now);

        Assert.Equal(7500m, claim.Reserve);
        Assert.Equal(ClaimEventType.ReserveAdjusted, claim.Events.Last().Type);
    }

    [Fact]
    public void Adjust_reserve_negative_is_validation() =>
        AssertViolation(RuleViolation.Validation, () => UnderReview().AdjustReserve(Officer1, -1m, Now));

    [Fact]
    public void Adjust_reserve_after_settle_is_conflict()
    {
        var claim = UnderReview();
        claim.Settle(Officer1, 4200m, Now);
        AssertViolation(RuleViolation.Conflict, () => claim.AdjustReserve(Officer1, 100m, Now));
    }

    // ---- Settle ----

    [Fact]
    public void Settle_closes_and_releases_reserve()
    {
        var claim = UnderReview();

        claim.Settle(Officer1, 4200m, Now);

        Assert.Equal(ClaimStatus.Settled, claim.Status);
        Assert.Equal(4200m, claim.SettlementAmount);
        Assert.Equal(0m, claim.Reserve);
        Assert.Equal(Now, claim.DecidedAt);
        Assert.False(claim.IsOpen);
        Assert.Equal(ClaimEventType.ClaimSettled, claim.Events.Last().Type);
    }

    [Fact]
    public void Settle_non_positive_is_validation() =>
        AssertViolation(RuleViolation.Validation, () => UnderReview().Settle(Officer1, 0m, Now));

    [Fact]
    public void Settle_while_awaiting_is_conflict()
    {
        var (claim, _) = Awaiting();
        AssertViolation(RuleViolation.Conflict, () => claim.Settle(Officer1, 4200m, Now));
    }

    [Fact]
    public void Settle_by_other_officer_is_forbidden() =>
        AssertViolation(RuleViolation.Forbidden, () => UnderReview().Settle(Officer2, 4200m, Now));

    [Fact]
    public void Acting_on_unassigned_claim_is_conflict() =>
        AssertViolation(RuleViolation.Conflict, () => NewClaim().Settle(Officer1, 4200m, Now));

    // ---- Reject ----

    [Fact]
    public void Reject_closes_with_reason()
    {
        var claim = UnderReview();

        claim.Reject(Officer1, "Not covered", Now);

        Assert.Equal(ClaimStatus.Rejected, claim.Status);
        Assert.Equal("Not covered", claim.RejectionReason);
        Assert.Equal(0m, claim.Reserve);
        Assert.Equal(ClaimEventType.ClaimRejected, claim.Events.Last().Type);
    }

    [Fact]
    public void Reject_blank_reason_is_validation() =>
        AssertViolation(RuleViolation.Validation, () => UnderReview().Reject(Officer1, " ", Now));

    [Fact]
    public void Reject_rejected_claim_is_conflict()
    {
        var claim = UnderReview();
        claim.Reject(Officer1, "Not covered", Now);
        AssertViolation(RuleViolation.Conflict, () => claim.Reject(Officer1, "Again", Now));
    }

    // ---- Events ----

    [Fact]
    public void Event_payload_writes_enums_as_strings()
    {
        var payload = NewClaim().Events.Single().Payload;

        Assert.Contains("\"market\":\"MY\"", payload);
        Assert.Contains("\"type\":\"Motor\"", payload);
    }

    [Fact]
    public void Each_action_appends_exactly_one_event()
    {
        var claim = NewClaim();
        Assert.Single(claim.Events);

        claim.Assign(Officer1, Now);
        Assert.Equal(2, claim.Events.Count);

        var request = claim.RequestInformation(Officer1, "Police report?", Now);
        Assert.Equal(3, claim.Events.Count);

        claim.RespondToInformationRequest(ClaimantA, request.Id, "Attached ref 123", Now);
        Assert.Equal(4, claim.Events.Count);

        claim.AdjustReserve(Officer1, 6000m, Now);
        Assert.Equal(5, claim.Events.Count);

        claim.Settle(Officer1, 5500m, Now);
        Assert.Equal(6, claim.Events.Count);
    }
}
