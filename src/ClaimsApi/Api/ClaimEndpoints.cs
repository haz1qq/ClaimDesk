using ClaimsApi.Data;
using ClaimsApi.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace ClaimsApi.Api;

/// <summary>
/// REST endpoints for claims (DESIGN.md §7). Handlers stay thin: check the role, load the claim,
/// call the domain method, save. Rule violations are thrown by the domain and mapped by <see cref="ErrorHandler"/>.
/// </summary>
public static class ClaimEndpoints
{
    public static void MapClaimEndpoints(this IEndpointRouteBuilder app)
    {
        var claims = app.MapGroup("/api/claims").AddEndpointFilter<CurrentUserFilter>();

        claims.MapPost("/", Submit);
        claims.MapGet("/", List);
        claims.MapGet("/{id:guid}", Get);

        claims.MapPost("/{id:guid}/assign", (Guid id, HttpContext http, ClaimsDbContext db, TimeProvider clock) =>
            Change(id, http, db, Role.Officer, (claim, user) => claim.Assign(user.Id, clock.GetUtcNow())));

        claims.MapPost("/{id:guid}/information-requests",
            (Guid id, RequestInformationRequest body, HttpContext http, ClaimsDbContext db, TimeProvider clock) =>
                Change(id, http, db, Role.Officer, (claim, user) => claim.RequestInformation(user.Id, body.Question, clock.GetUtcNow())));

        claims.MapPost("/{id:guid}/information-requests/{requestId:guid}/response",
            (Guid id, Guid requestId, RespondRequest body, HttpContext http, ClaimsDbContext db, TimeProvider clock) =>
                Change(id, http, db, Role.Claimant, (claim, user) =>
                    claim.RespondToInformationRequest(user.Id, requestId, body.Response, clock.GetUtcNow())));

        claims.MapPut("/{id:guid}/reserve",
            (Guid id, AdjustReserveRequest body, HttpContext http, ClaimsDbContext db, TimeProvider clock) =>
                Change(id, http, db, Role.Officer, (claim, user) => claim.AdjustReserve(user.Id, body.Reserve, clock.GetUtcNow())));

        claims.MapPost("/{id:guid}/settle",
            (Guid id, SettleRequest body, HttpContext http, ClaimsDbContext db, TimeProvider clock) =>
                Change(id, http, db, Role.Officer, (claim, user) => claim.Settle(user.Id, body.Amount, clock.GetUtcNow())));

        claims.MapPost("/{id:guid}/reject",
            (Guid id, RejectRequest body, HttpContext http, ClaimsDbContext db, TimeProvider clock) =>
                Change(id, http, db, Role.Officer, (claim, user) => claim.Reject(user.Id, body.Reason, clock.GetUtcNow())));
    }

    static async Task<Created<ClaimDetail>> Submit(
        SubmitClaimRequest body, HttpContext http, ClaimsDbContext db, TimeProvider clock)
    {
        var user = http.CurrentUser();
        user.RequireRole(Role.Claimant);
        // Claimant and market come from the identified user, never from the request body.
        var market = user.Market ?? throw ClaimRuleException.Validation("Your profile has no market.");

        var claim = Claim.Submit(user.Id, market, body.Type, body.IncidentDate, body.Description, body.ClaimedAmount,
            clock.GetUtcNow());
        db.Claims.Add(claim);
        await db.SaveChangesAsync();

        return TypedResults.Created($"/api/claims/{claim.Id}", await ToDetail(db, claim, user));
    }

    static async Task<List<ClaimSummary>> List(HttpContext http, ClaimsDbContext db, ClaimStatus? status)
    {
        var user = http.CurrentUser();
        var query = db.Claims.AsNoTracking();

        query = user.Role switch
        {
            Role.Claimant => query.Where(c => c.ClaimantId == user.Id),
            // Officers see the unassigned queue plus their own claims.
            Role.Officer => query.Where(c => c.Status == ClaimStatus.Submitted || c.AssignedOfficerId == user.Id),
            _ => query, // managers see everything
        };
        if (status is not null)
            query = query.Where(c => c.Status == status);

        var claims = await query.OrderByDescending(c => c.CreatedAt).ToListAsync();
        var names = await UserNames(db);
        return claims.Select(c => ToSummary(c, names, user)).ToList();
    }

    static async Task<Results<Ok<ClaimDetail>, ProblemHttpResult>> Get(Guid id, HttpContext http, ClaimsDbContext db)
    {
        var user = http.CurrentUser();
        var claim = await db.Claims.AsNoTracking()
            .Include(c => c.InformationRequests)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (claim is null)
            return ClaimNotFound();
        if (user.Role == Role.Claimant && claim.ClaimantId != user.Id)
            throw ClaimRuleException.Forbidden("You can only view your own claims.");

        return TypedResults.Ok(await ToDetail(db, claim, user));
    }

    /// <summary>The shared shape of every action: role check → load → domain method → save → return the claim.</summary>
    static async Task<Results<Ok<ClaimDetail>, ProblemHttpResult>> Change(
        Guid id, HttpContext http, ClaimsDbContext db, Role requiredRole, Action<Claim, User> change)
    {
        var user = http.CurrentUser();
        user.RequireRole(requiredRole);

        var claim = await db.Claims.Include(c => c.InformationRequests).FirstOrDefaultAsync(c => c.Id == id);
        if (claim is null)
            return ClaimNotFound();

        change(claim, user);
        await db.SaveChangesAsync(); // throws DbUpdateConcurrencyException if someone else saved first (xmin)

        return TypedResults.Ok(await ToDetail(db, claim, user));
    }

    static ProblemHttpResult ClaimNotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Claim not found");

    // ---- Mapping to DTOs ----

    // There are only a handful of users, so one small lookup beats a join per query.
    static Task<Dictionary<Guid, string>> UserNames(ClaimsDbContext db) =>
        db.Users.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Name);

    static ClaimSummary ToSummary(Claim c, Dictionary<Guid, string> names, User viewer) => new(
        c.Id, c.DisplayNumber, c.Status, c.Type, c.Market, c.Currency, c.ClaimedAmount,
        viewer.Role == Role.Claimant ? null : c.Reserve, // the reserve is internal: never sent to claimants
        c.ClaimantId, names.GetValueOrDefault(c.ClaimantId, "Unknown"),
        c.AssignedOfficerId, c.AssignedOfficerId is { } officer ? names.GetValueOrDefault(officer, "Unknown") : null,
        c.CreatedAt, c.UpdatedAt);

    static async Task<ClaimDetail> ToDetail(ClaimsDbContext db, Claim claim, User viewer)
    {
        var names = await UserNames(db);
        var events = await db.ClaimEvents.AsNoTracking()
            .Where(e => e.ClaimId == claim.Id)
            .Where(e => viewer.Role != Role.Claimant || e.Type != ClaimEventType.ReserveAdjusted) // internal to the insurer
            .OrderBy(e => e.OccurredAt)
            .ToListAsync();

        return new ClaimDetail(
            ToSummary(claim, names, viewer), claim.IncidentDate, claim.Description, claim.SettlementAmount,
            claim.RejectionReason, claim.DecidedAt,
            claim.InformationRequests.OrderBy(r => r.RequestedAt)
                .Select(r => new InformationRequestDto(r.Id, r.Question, r.Response, r.RequestedAt, r.RespondedAt))
                .ToList(),
            events.Select(e => new TimelineEntry(e.Type, e.ActorId, names.GetValueOrDefault(e.ActorId, "Unknown"), e.OccurredAt))
                .ToList());
    }
}
