using System.Globalization;

namespace NotificationsService;

/// <summary>What the claimant is told for each event. Null = this event doesn't notify the claimant.</summary>
public static class NotificationText
{
    public static string? For(ClaimEventMessage e) => e.Type switch
    {
        "ClaimSubmitted" => $"We've received your claim {e.ClaimNumber}.",
        "ClaimAssigned" => $"Your claim {e.ClaimNumber} is now being reviewed by a claims officer.",
        "InformationRequested" => $"We need more information for claim {e.ClaimNumber}: {e.Data.GetProperty("question").GetString()}",
        "ClaimSettled" => string.Create(CultureInfo.InvariantCulture,
            $"Your claim {e.ClaimNumber} has been settled for {e.Data.GetProperty("currency").GetString()} {e.Data.GetProperty("amount").GetDecimal():N2}."),
        "ClaimRejected" => $"Your claim {e.ClaimNumber} was not approved: {e.Data.GetProperty("reason").GetString()}",
        _ => null, // InformationProvided, ReserveAdjusted: internal, not sent to the claimant
    };
}
