using ClaimsApi.Data;
using Microsoft.EntityFrameworkCore;

namespace ClaimsApi.Api;

public static class UserEndpoints
{
    /// <summary>The seeded users, for the role switcher. Open to anyone: it runs before a user is chosen.</summary>
    public static void MapUserEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/users", async (ClaimsDbContext db) =>
            await db.Users.AsNoTracking()
                .OrderBy(u => u.Role).ThenBy(u => u.Name)
                .Select(u => new UserDto(u.Id, u.Name, u.Role, u.Market))
                .ToListAsync());
}
