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
