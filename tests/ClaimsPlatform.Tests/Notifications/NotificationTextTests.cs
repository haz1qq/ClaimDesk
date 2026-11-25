using System.Text.Json;
using NotificationsService;

namespace ClaimsPlatform.Tests.Notifications;

public class NotificationTextTests
{
    static ClaimEventMessage Event(string type, string data = "{}") => new(
        Guid.NewGuid(), type, Guid.NewGuid(), "CLM-000007", Guid.NewGuid(),
        new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero), JsonDocument.Parse(data).RootElement);

    [Fact]
    public void Submitted() =>
        Assert.Equal("We've received your claim CLM-000007.", NotificationText.For(Event("ClaimSubmitted")));

    [Fact]
    public void Assigned() =>
        Assert.Equal("Your claim CLM-000007 is now being reviewed by a claims officer.",
            NotificationText.For(Event("ClaimAssigned")));

    [Fact]
    public void Information_requested_includes_the_question() =>
        Assert.Equal("We need more information for claim CLM-000007: Police report?",
            NotificationText.For(Event("InformationRequested", """{"requestId":"00000000-0000-0000-0000-000000000001","question":"Police report?"}""")));

    [Fact]
    public void Settled_includes_currency_and_amount() =>
        Assert.Equal("Your claim CLM-000007 has been settled for MYR 5,500.00.",
            NotificationText.For(Event("ClaimSettled", """{"amount":5500,"currency":"MYR"}""")));

    [Fact]
    public void Rejected_includes_the_reason() =>
        Assert.Equal("Your claim CLM-000007 was not approved: Not covered",
            NotificationText.For(Event("ClaimRejected", """{"reason":"Not covered"}""")));

    // A message missing a detail must still produce sensible text, never throw (a throw would stall the consumer).
    [Theory]
    [InlineData("InformationRequested", "We need more information for claim CLM-000007.")]
    [InlineData("ClaimSettled", "Your claim CLM-000007 has been settled.")]
    [InlineData("ClaimRejected", "Your claim CLM-000007 was not approved.")]
    public void Missing_details_fall_back_to_generic_text(string type, string expected) =>
        Assert.Equal(expected, NotificationText.For(Event(type, "{}")));

    [Fact]
    public void Well_formed_event_is_valid() => Assert.True(Event("ClaimSubmitted").IsValid);

    [Fact]
    public void Event_without_recipient_is_invalid() =>
        Assert.False((Event("ClaimSubmitted") with { ClaimantId = Guid.Empty }).IsValid);

    [Fact]
    public void Event_without_id_is_invalid() =>
        Assert.False((Event("ClaimSubmitted") with { EventId = Guid.Empty }).IsValid);

    [Fact]
    public void Event_with_oversized_claim_number_is_invalid() =>
        Assert.False((Event("ClaimSubmitted") with { ClaimNumber = new string('9', 21) }).IsValid);

    [Fact]
    public void Reserve_adjustment_is_not_sent_to_the_claimant() =>
        Assert.Null(NotificationText.For(Event("ReserveAdjusted", """{"previous":1,"current":2,"currency":"MYR"}""")));
}
