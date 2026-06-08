using Microsoft.EntityFrameworkCore;
using VigiShield.Domain.Entities;

namespace VigiShield.Infrastructure.Persistence;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Household> Households => Set<Household>();
    public DbSet<SecurityEvent> Events => Set<SecurityEvent>();
    public DbSet<AuthorizedFace> AuthorizedFaces => Set<AuthorizedFace>();
    public DbSet<AlertConfig> AlertConfigs => Set<AlertConfig>();
    public DbSet<CameraConfig> CameraConfigs => Set<CameraConfig>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Role).HasConversion<string>();
            e.HasOne(u => u.Household)
             .WithMany(h => h.Users)
             .HasForeignKey(u => u.HouseholdId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Household>(e =>
        {
            e.HasKey(h => h.Id);
        });

        modelBuilder.Entity<SecurityEvent>(e =>
        {
            e.HasKey(ev => ev.Id);
            e.Property(ev => ev.EventType).HasConversion<string>();
            e.Property(ev => ev.RiskLevel).HasConversion<string>();
            e.HasOne(ev => ev.Household)
             .WithMany(h => h.Events)
             .HasForeignKey(ev => ev.HouseholdId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(ev => ev.Camera)
             .WithMany()
             .HasForeignKey(ev => ev.CameraId)
             .OnDelete(DeleteBehavior.SetNull)
             .IsRequired(false);
        });

        modelBuilder.Entity<AuthorizedFace>(e =>
        {
            e.HasKey(f => f.Id);
            e.HasOne(f => f.Household)
             .WithMany(h => h.AuthorizedFaces)
             .HasForeignKey(f => f.HouseholdId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AlertConfig>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasOne(c => c.Household)
             .WithOne(h => h.AlertConfig)
             .HasForeignKey<AlertConfig>(c => c.HouseholdId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── CameraConfig: one-to-MANY (household can have multiple cameras) ──
        modelBuilder.Entity<CameraConfig>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.StreamMode).HasConversion<string>();
            e.HasOne(c => c.Household)
             .WithMany(h => h.CameraConfigs)
             .HasForeignKey(c => c.HouseholdId)
             .OnDelete(DeleteBehavior.Cascade);
            // Non-unique index (multiple cameras per household allowed)
            e.HasIndex(c => c.HouseholdId);
        });

        modelBuilder.Entity<Invitation>(e =>
        {
            e.HasKey(i => i.Id);
            e.HasIndex(i => i.Token).IsUnique();
            e.Ignore(i => i.IsExpired);
            e.Ignore(i => i.IsAccepted);
            e.HasOne(i => i.Household)
             .WithMany(h => h.Invitations)
             .HasForeignKey(i => i.HouseholdId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<NotificationLog>(e =>
        {
            e.HasKey(n => n.Id);
            e.Property(n => n.Channel).HasConversion<string>();
            e.HasOne(n => n.Household)
             .WithMany(h => h.NotificationLogs)
             .HasForeignKey(n => n.HouseholdId)
             .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(n => n.SecurityEvent)
             .WithMany()
             .HasForeignKey(n => n.SecurityEventId)
             .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(n => n.Recipient)
             .WithMany()
             .HasForeignKey(n => n.RecipientUserId)
             .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
