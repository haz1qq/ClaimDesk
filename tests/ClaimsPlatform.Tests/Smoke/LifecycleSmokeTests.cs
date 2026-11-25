using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ClaimsApi.Api;
using ClaimsApi.Data;
using ClaimsApi.Domain;

namespace ClaimsPlatform.Tests.Smoke;

/// <summary>
/// End-to-end checks against a RUNNING Claims API (http://localhost:5100) and the compose infrastructure.
/// Run with: dotnet test --filter Category=Smoke
/// All smoke tests live in this one class so xUnit runs them one at a time.
/// </summary>
[Trait("Category", "Smoke")]
public class LifecycleSmokeTests
{
    static readonly Uri BaseAddress = new("http://localhost:5100");

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    static HttpClient Anonymous() => new() { BaseAddress = BaseAddress };

    static HttpClient As(Guid userId)
    {
        var client = Anonymous();
        client.DefaultRequestHeaders.Add("X-User-Id", userId.ToString());
        return client;
    }

    static DateOnly Yesterday => DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));

    static async Task<HttpResponseMessage> Send(Guid userId, HttpMethod method, string path, object? body = null)
    {
        using var http = As(userId);
        var request = new HttpRequestMessage(method, path);
        if (body is not null)
            request.Content = JsonContent.Create(body, options: Json);
        return await http.SendAsync(request);
    }

    /// <summary>Sends a request, asserts the status code, and returns the claim from the response.</summary>
    static async Task<ClaimDetail> Expect(HttpStatusCode expected, Guid userId, HttpMethod method, string path, object? body = null)
    {
        var response = await Send(userId, method, path, body);
        Assert.True(response.StatusCode == expected,
            $"{method} {path}: expected {(int)expected}, got {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<ClaimDetail>(Json))!;
    }

    static Task<ClaimDetail> SubmitAsAisha(decimal amount = 5000m) =>
        Expect(HttpStatusCode.Created, SeedUsers.ClaimantAisha, HttpMethod.Post, "/api/claims",
            new SubmitClaimRequest(ClaimType.Motor, Yesterday, "Smoke test: scraped bumper", amount));

    [Fact]
    public async Task Full_lifecycle_settle()
    {
        var claim = await SubmitAsAisha();
        var id = claim.Summary.Id;
        Assert.Equal(ClaimStatus.Submitted, claim.Summary.Status);
        Assert.StartsWith("CLM-", claim.Summary.Number);

        claim = await Expect(HttpStatusCode.OK, SeedUsers.OfficerMei, HttpMethod.Post, $"/api/claims/{id}/assign");
        Assert.Equal(ClaimStatus.UnderReview, claim.Summary.Status);

        claim = await Expect(HttpStatusCode.OK, SeedUsers.OfficerMei, HttpMethod.Post, $"/api/claims/{id}/information-requests",
            new RequestInformationRequest("Photos of the damage?"));
        Assert.Equal(ClaimStatus.AwaitingInformation, claim.Summary.Status);
        var requestId = Assert.Single(claim.InformationRequests).Id;

        claim = await Expect(HttpStatusCode.OK, SeedUsers.ClaimantAisha, HttpMethod.Post,
            $"/api/claims/{id}/information-requests/{requestId}/response", new RespondRequest("Uploaded to the portal."));
        Assert.Equal(ClaimStatus.UnderReview, claim.Summary.Status);

        claim = await Expect(HttpStatusCode.OK, SeedUsers.OfficerMei, HttpMethod.Put, $"/api/claims/{id}/reserve",
            new AdjustReserveRequest(6000m));
        Assert.Equal(6000m, claim.Summary.Reserve);

        claim = await Expect(HttpStatusCode.OK, SeedUsers.OfficerMei, HttpMethod.Post, $"/api/claims/{id}/settle",
            new SettleRequest(5500m));
        Assert.Equal(ClaimStatus.Settled, claim.Summary.Status);
        Assert.Equal(0m, claim.Summary.Reserve);
        Assert.Equal(
            [ClaimEventType.ClaimSubmitted, ClaimEventType.ClaimAssigned, ClaimEventType.InformationRequested,
             ClaimEventType.InformationProvided, ClaimEventType.ReserveAdjusted, ClaimEventType.ClaimSettled],
            claim.Timeline.Select(t => t.Type));
    }

    [Fact]
    public async Task Reject_path()
    {
        var id = (await SubmitAsAisha()).Summary.Id;
        await Expect(HttpStatusCode.OK, SeedUsers.OfficerMei, HttpMethod.Post, $"/api/claims/{id}/assign");

        var claim = await Expect(HttpStatusCode.OK, SeedUsers.OfficerMei, HttpMethod.Post, $"/api/claims/{id}/reject",
            new RejectRequest("Not covered"));

        Assert.Equal(ClaimStatus.Rejected, claim.Summary.Status);
        Assert.Equal("Not covered", claim.RejectionReason);
    }

    [Fact]
    public async Task Concurrent_assign_has_one_winner()
    {
        var id = (await SubmitAsAisha()).Summary.Id;

        var responses = await Task.WhenAll(
            Send(SeedUsers.OfficerMei, HttpMethod.Post, $"/api/claims/{id}/assign"),
            Send(SeedUsers.OfficerArjun, HttpMethod.Post, $"/api/claims/{id}/assign"));

        Assert.Equal([200, 409], responses.Select(r => (int)r.StatusCode).Order());
    }

    [Fact]
    public async Task Unknown_user_is_401()
    {
        using var anonymous = Anonymous();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/claims")).StatusCode);

        var stranger = await Send(Guid.NewGuid(), HttpMethod.Get, "/api/claims");
        Assert.Equal(HttpStatusCode.Unauthorized, stranger.StatusCode);
    }

    [Fact]
    public async Task Other_claimant_is_403()
    {
        var id = (await SubmitAsAisha()).Summary.Id;

        var response = await Send(SeedUsers.ClaimantDaniel, HttpMethod.Get, $"/api/claims/{id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Officer_cannot_submit()
    {
        var response = await Send(SeedUsers.OfficerMei, HttpMethod.Post, "/api/claims",
            new SubmitClaimRequest(ClaimType.Motor, Yesterday, "Officer trying to submit", 100m));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Claimant_cannot_assign_is_403()
    {
        var id = (await SubmitAsAisha()).Summary.Id;

        var response = await Send(SeedUsers.ClaimantAisha, HttpMethod.Post, $"/api/claims/{id}/assign");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Settle_unassigned_claim_is_409()
    {
        var id = (await SubmitAsAisha()).Summary.Id;

        var response = await Send(SeedUsers.OfficerMei, HttpMethod.Post, $"/api/claims/{id}/settle", new SettleRequest(100m));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_enum_is_400()
    {
        using var http = As(SeedUsers.ClaimantAisha);
        var body = new StringContent(
            """{"type":"Boat","incidentDate":"2026-11-20","description":"x","claimedAmount":100}""",
            System.Text.Encoding.UTF8, "application/json");

        var response = await http.PostAsync("/api/claims", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Users_lists_seeded_users_with_string_roles()
    {
        using var http = Anonymous();

        var users = await http.GetFromJsonAsync<List<UserDto>>("/api/users", Json);
        var raw = await http.GetStringAsync("/api/users");

        Assert.Equal(5, users!.Count);
        Assert.Contains(users, u => u.Id == SeedUsers.OfficerMei && u.Role == Role.Officer);
        Assert.Contains(users, u => u.Id == SeedUsers.ClaimantAisha && u.Market == Market.MY);
        Assert.Contains("\"role\":\"Officer\"", raw);
    }
}
