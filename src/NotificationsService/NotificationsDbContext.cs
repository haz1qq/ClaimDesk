using Microsoft.EntityFrameworkCore;

namespace NotificationsService;

/// <summary>This service's own database ("notifications"). It never touches the Claims API's tables.</summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<Notification>(notification =>
        {
            notification.Property(n => n.Id).ValueGeneratedNever();
            notification.Property(n => n.ClaimNumber).HasMaxLength(20);
            notification.Property(n => n.Message).HasMaxLength(2000);
            notification.HasIndex(n => new { n.RecipientId, n.CreatedAt });
        });

        model.Entity<ProcessedEvent>(processed =>
        {
            // The primary key on EventId is what makes a duplicate insert impossible.
            processed.HasKey(p => p.EventId);
            processed.Property(p => p.EventId).ValueGeneratedNever();
        });
    }
}
