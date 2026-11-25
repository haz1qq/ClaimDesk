using ClaimsApi.Data;
using ClaimsApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClaimsApi.Api;

/// <summary>Team workload and liability exposure for officers and managers (DESIGN.md §7). Plain queries, no cache.</summary>
public static class DashboardEndpoints
{
    public static void MapDashboardEndpoints(this IEndpointRouteBuilder app) =>
        app.MapGet("/api/dashboard", Get).AddEndpointFilter<CurrentUserFilter>();

    static async Task<DashboardDto> Get(HttpContext http, ClaimsDbContext db, TimeProvider clock)
    {
        http.CurrentUser().RequireRole(Role.Officer, Role.Manager);

        var now = clock.GetUtcNow();
        var weekAgo = now.AddDays(-7);
        var monthAgo = now.AddDays(-30);

        var open = db.Claims.AsNoTracking().Where(c =>
            c.Status == ClaimStatus.Submitted || c.Status == ClaimStatus.UnderReview || c.Status == ClaimStatus.AwaitingInformation);

        var byStatus = await open
            .GroupBy(c => c.Status)
            .Select(g => new StatusCount(g.Key, g.Count()))
            .ToListAsync();

        // Liability exposure = the reserves on open claims, summed per currency (never converted).
        var exposure = await open
            .GroupBy(c => c.Currency)
            .OrderBy(g => g.Key) // sort before projecting: EF can't translate ordering on a constructed record
            .Select(g => new CurrencyExposure(g.Key, g.Sum(c => c.Reserve), g.Count()))
            .ToListAsync();

        var unassigned = await open.CountAsync(c => c.AssignedOfficerId == null);
        var openOver7Days = await open.CountAsync(c => c.CreatedAt < weekAgo);
        var openOver30Days = await open.CountAsync(c => c.CreatedAt < monthAgo);

        var openPerOfficer = await open
            .Where(c => c.AssignedOfficerId != null)
            .GroupBy(c => c.AssignedOfficerId!.Value)
            .Select(g => new { OfficerId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.OfficerId, x => x.Count);

        // Decided in the last 30 days: a small set, so the averages are worked out in memory.
        var decided = await db.Claims.AsNoTracking()
            .Where(c => c.DecidedAt >= monthAgo && c.AssignedOfficerId != null)
            .Select(c => new { OfficerId = c.AssignedOfficerId!.Value, c.CreatedAt, DecidedAt = c.DecidedAt!.Value })
            .ToListAsync();

        var officers = await db.Users.AsNoTracking()
            .Where(u => u.Role == Role.Officer)
            .OrderBy(u => u.Name)
            .ToListAsync();

        var workload = officers.Select(officer =>
        {
            var mine = decided.Where(d => d.OfficerId == officer.Id).ToList();
            double? avgDays = mine.Count == 0 ? null : Math.Round(mine.Average(d => (d.DecidedAt - d.CreatedAt).TotalDays), 1);
            return new OfficerWorkload(officer.Id, officer.Name, openPerOfficer.GetValueOrDefault(officer.Id), mine.Count, avgDays);
        }).ToList();

        return new DashboardDto(byStatus, unassigned, workload, exposure, openOver7Days, openOver30Days);
    }
}
