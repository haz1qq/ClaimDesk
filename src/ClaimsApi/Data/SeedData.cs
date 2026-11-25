using ClaimsApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClaimsApi.Data;

/// <summary>Fixed ids so the front end, smoke tests and README can refer to the same people.</summary>
public static class SeedUsers
{
    public static readonly Guid ClaimantAisha = Guid.Parse("00000000-0000-0000-0000-00000000c001");
    public static readonly Guid ClaimantDaniel = Guid.Parse("00000000-0000-0000-0000-00000000c002");
    public static readonly Guid OfficerMei = Guid.Parse("00000000-0000-0000-0000-00000000f001");
    public static readonly Guid OfficerArjun = Guid.Parse("00000000-0000-0000-0000-00000000f002");
    public static readonly Guid ManagerJames = Guid.Parse("00000000-0000-0000-0000-00000000a001");
}

public static class SeedData
{
    /// <summary>Seeds demo users and claims once (only when there are no users yet).</summary>
    public static async Task EnsureSeededAsync(ClaimsDbContext db, DateTimeOffset now)
    {
        if (await db.Users.AnyAsync())
            return;

        db.Users.AddRange(
            new User(SeedUsers.ClaimantAisha, "Aisha Rahman", Role.Claimant, Market.MY),
            new User(SeedUsers.ClaimantDaniel, "Daniel Tan", Role.Claimant, Market.SG),
            new User(SeedUsers.OfficerMei, "Mei Lin", Role.Officer),
            new User(SeedUsers.OfficerArjun, "Arjun Patel", Role.Officer),
            new User(SeedUsers.ManagerJames, "James Lee", Role.Manager));

        // Claims are built through the domain methods so each one has a real event history.
        DateTimeOffset DaysAgo(int days) => now.AddDays(-days);
        Claim Submit(Guid claimant, Market market, ClaimType type, string description, decimal amount, int daysAgo) =>
            Claim.Submit(claimant, market, type, DateOnly.FromDateTime(DaysAgo(daysAgo + 1).UtcDateTime), description, amount, DaysAgo(daysAgo));

        var mirror = Submit(SeedUsers.ClaimantAisha, Market.MY, ClaimType.Motor, "Side mirror knocked off in a car park.", 850m, 1);
        var ceiling = Submit(SeedUsers.ClaimantDaniel, Market.SG, ClaimType.Property, "Water leak from the unit upstairs damaged the ceiling.", 3200m, 2);

        var kitchen = Submit(SeedUsers.ClaimantAisha, Market.MY, ClaimType.Property, "Burst pipe flooded the kitchen.", 12000m, 9);
        kitchen.Assign(SeedUsers.OfficerMei, DaysAgo(8));

        var rearEnd = Submit(SeedUsers.ClaimantDaniel, Market.SG, ClaimType.Motor, "Rear-ended at traffic lights.", 4500m, 12);
        rearEnd.Assign(SeedUsers.OfficerArjun, DaysAgo(11));
        rearEnd.RequestInformation(SeedUsers.OfficerArjun, "Please share the police report number.", DaysAgo(10));

        var windscreen = Submit(SeedUsers.ClaimantAisha, Market.MY, ClaimType.Motor, "Windscreen cracked by road debris.", 600m, 20);
        windscreen.Assign(SeedUsers.OfficerMei, DaysAgo(19));
        windscreen.Settle(SeedUsers.OfficerMei, 600m, DaysAgo(15));

        var phone = Submit(SeedUsers.ClaimantDaniel, Market.SG, ClaimType.Property, "Phone dropped and screen broken at home.", 1500m, 25);
        phone.Assign(SeedUsers.OfficerArjun, DaysAgo(24));
        phone.Reject(SeedUsers.OfficerArjun, "Accidental damage to portable devices is not covered.", DaysAgo(22));

        db.Claims.AddRange(mirror, ceiling, kitchen, rearEnd, windscreen, phone);
        await db.SaveChangesAsync();
    }
}
