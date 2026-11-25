using ClaimsApi.Domain;
using Microsoft.EntityFrameworkCore;

namespace ClaimsApi.Data;

public sealed class ClaimsDbContext(DbContextOptions<ClaimsDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Claim> Claims => Set<Claim>();
    public DbSet<InformationRequest> InformationRequests => Set<InformationRequest>();
    public DbSet<ClaimEvent> ClaimEvents => Set<ClaimEvent>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configuration)
    {
        // Store enums as readable text ("UnderReview") rather than numbers.
        configuration.Properties<Market>().HaveConversion<string>().HaveMaxLength(2);
        configuration.Properties<ClaimType>().HaveConversion<string>().HaveMaxLength(20);
        configuration.Properties<ClaimStatus>().HaveConversion<string>().HaveMaxLength(30);
        configuration.Properties<Role>().HaveConversion<string>().HaveMaxLength(20);
        configuration.Properties<ClaimEventType>().HaveConversion<string>().HaveMaxLength(40);

        // Money: numeric(18,2).
        configuration.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<User>(user =>
        {
            user.Property(u => u.Id).ValueGeneratedNever();
            user.Property(u => u.Name).HasMaxLength(100);
        });

        model.Entity<Claim>(claim =>
        {
            // Ids are created in the domain, not by the database. Without ValueGeneratedNever, EF would treat
            // a new child (e.g. an InformationRequest added to a loaded claim) as existing and try to UPDATE it.
            claim.Property(c => c.Id).ValueGeneratedNever();
            claim.Property(c => c.Number).UseIdentityAlwaysColumn();
            claim.HasIndex(c => c.Number).IsUnique();
            claim.Property(c => c.Currency).HasMaxLength(3);
            claim.Property(c => c.Description).HasMaxLength(2000);
            claim.Property(c => c.RejectionReason).HasMaxLength(1000);
            claim.Property(c => c.Version).IsRowVersion(); // Npgsql maps a uint row version to Postgres xmin
            claim.Ignore(c => c.DisplayNumber);
            claim.Ignore(c => c.IsOpen);

            claim.HasOne<User>().WithMany().HasForeignKey(c => c.ClaimantId).OnDelete(DeleteBehavior.Restrict);
            claim.HasOne<User>().WithMany().HasForeignKey(c => c.AssignedOfficerId).OnDelete(DeleteBehavior.Restrict);
            claim.HasMany(c => c.InformationRequests).WithOne().HasForeignKey(r => r.ClaimId).OnDelete(DeleteBehavior.Cascade);
            claim.HasMany(c => c.Events).WithOne().HasForeignKey(e => e.ClaimId).OnDelete(DeleteBehavior.Cascade);

            claim.HasIndex(c => c.ClaimantId);
            claim.HasIndex(c => new { c.Status, c.AssignedOfficerId });
        });

        model.Entity<InformationRequest>(request =>
        {
            request.Property(r => r.Id).ValueGeneratedNever();
            request.Property(r => r.Question).HasMaxLength(1000);
            request.Property(r => r.Response).HasMaxLength(2000);
            request.Ignore(r => r.IsOpen);
        });

        model.Entity<ClaimEvent>(claimEvent =>
        {
            claimEvent.Property(e => e.Id).ValueGeneratedNever();
            claimEvent.Property(e => e.Payload).HasColumnType("jsonb");
            claimEvent.HasIndex(e => new { e.PublishedAt, e.OccurredAt }); // the outbox poll
        });
    }
}
