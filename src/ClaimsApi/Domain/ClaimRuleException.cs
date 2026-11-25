namespace ClaimsApi.Domain;

/// <summary>A business rule was broken. <see cref="Kind"/> decides the HTTP status.</summary>
public sealed class ClaimRuleException(RuleViolation kind, string message) : Exception(message)
{
    public RuleViolation Kind { get; } = kind;

    public static ClaimRuleException Validation(string message) => new(RuleViolation.Validation, message);
    public static ClaimRuleException Forbidden(string message) => new(RuleViolation.Forbidden, message);
    public static ClaimRuleException Conflict(string message) => new(RuleViolation.Conflict, message);
}
