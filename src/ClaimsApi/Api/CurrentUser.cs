using ClaimsApi.Data;
using ClaimsApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClaimsApi.Api;

/// <summary>
/// Identifies the caller from the X-User-Id header and rejects unknown callers with 401.
/// This stands in for real authentication (OIDC/JWT in production; see DESIGN.md §2).
/// </summary>
public sealed class CurrentUserFilter : IEndpointFilter
{
    public const string Header = "X-User-Id";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var http = context.HttpContext;
        var db = http.RequestServices.GetRequiredService<ClaimsDbContext>();

        var user = Guid.TryParse(http.Request.Headers[Header], out var userId)
            ? await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId)
            : null;

        if (user is null)
            return TypedResults.Problem(statusCode: StatusCodes.Status401Unauthorized,
                title: "Unknown user", detail: $"Send a known user id in the {Header} header.");

        http.Items[typeof(User)] = user;
        return await next(context);
    }
}

public static class CurrentUserExtensions
{
    /// <summary>The caller identified by <see cref="CurrentUserFilter"/>.</summary>
    public static User CurrentUser(this HttpContext http) => (User)http.Items[typeof(User)]!;

    public static void RequireRole(this User user, params Role[] roles)
    {
        if (!roles.Contains(user.Role))
            throw ClaimRuleException.Forbidden($"This action needs the {string.Join(" or ", roles)} role.");
    }
}
