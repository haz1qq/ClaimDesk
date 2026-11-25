using ClaimsApi.Domain;

namespace ClaimsApi.Api;

// The REST contract. The Angular app mirrors these in web/src/app/core/models.ts.

public record UserDto(Guid Id, string Name, Role Role, Market? Market);

// ---- Requests ----

/// <summary>Claimant and market are NOT in the body: they come from the identified user.</summary>
public record SubmitClaimRequest(ClaimType Type, DateOnly IncidentDate, string Description, decimal ClaimedAmount);
public record RequestInformationRequest(string Question);
public record RespondRequest(string Response);
public record AdjustReserveRequest(decimal Reserve);
public record SettleRequest(decimal Amount);
public record RejectRequest(string Reason);

// ---- Responses ----

public record ClaimSummary(
    Guid Id, string Number, ClaimStatus Status, ClaimType Type, Market Market, string Currency,
    decimal ClaimedAmount, decimal Reserve, Guid ClaimantId, string ClaimantName,
    Guid? AssignedOfficerId, string? AssignedOfficerName, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public record InformationRequestDto(
    Guid Id, string Question, string? Response, DateTimeOffset RequestedAt, DateTimeOffset? RespondedAt);

public record TimelineEntry(ClaimEventType Type, Guid ActorId, string ActorName, DateTimeOffset OccurredAt);

public record ClaimDetail(
    ClaimSummary Summary, DateOnly IncidentDate, string Description, decimal? SettlementAmount,
    string? RejectionReason, DateTimeOffset? DecidedAt,
    IReadOnlyList<InformationRequestDto> InformationRequests, IReadOnlyList<TimelineEntry> Timeline);
