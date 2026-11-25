namespace ClaimsApi.Domain;

public enum Market { SG, MY, HK, TH, ID, AU }

public enum ClaimType { Motor, Property }

public enum ClaimStatus { Submitted, UnderReview, AwaitingInformation, Settled, Rejected }

public enum Role { Claimant, Officer, Manager }

public enum ClaimEventType
{
    ClaimSubmitted,
    ClaimAssigned,
    InformationRequested,
    InformationProvided,
    ReserveAdjusted,
    ClaimSettled,
    ClaimRejected,
}

/// <summary>Which kind of rule was broken. The API maps these to 400 / 403 / 409.</summary>
public enum RuleViolation { Validation, Forbidden, Conflict }
