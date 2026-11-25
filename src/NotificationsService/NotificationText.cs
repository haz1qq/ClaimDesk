using System.Globalization;
using System.Text.Json;

namespace NotificationsService;

/// <summary>What the claimant is told for each event. Null = this event doesn't notify the claimant.</summary>
public static class NotificationText
{
    public static string? For(ClaimEventMessage e)
    {
        var n = e.ClaimNumber;
        return e.Type switch
        {
            "ClaimSubmitted" => $"We've received your claim {n}.",
            "ClaimAssigned" => $"Your claim {n} is now being reviewed by a claims officer.",
            "InformationRequested" => Text(e.Data, "question") is { } question
                ? $"We need more information for claim {n}: {question}"
                : $"We need more information for claim {n}.",
            "ClaimSettled" => Text(e.Data, "currency") is { } currency && Amount(e.Data) is { } amount
                ? string.Create(CultureInfo.InvariantCulture, $"Your claim {n} has been settled for {currency} {amount:N2}.")
                : $"Your claim {n} has been settled.",
            "ClaimRejected" => Text(e.Data, "reason") is { } reason
                ? $"Your claim {n} was not approved: {reason}"
                : $"Your claim {n} was not approved.",
            _ => null, // InformationProvided, ReserveAdjusted: internal, not sent to the claimant
        };
    }

    // Missing or wrongly typed details give the generic sentence instead of throwing.
    static string? Text(JsonElement data, string name) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    static decimal? Amount(JsonElement data) =>
        data.ValueKind == JsonValueKind.Object && data.TryGetProperty("amount", out var value) && value.TryGetDecimal(out var amount)
            ? amount
            : null;
}
