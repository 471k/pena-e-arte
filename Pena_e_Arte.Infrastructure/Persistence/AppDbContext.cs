using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Persistence;

public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentTenant tenant) : IdentityDbContext<IdentityUser>(options), IAppDbContext
{
    // --- Tenant-scoped ---
    public DbSet<Appointment>     Appointments     => Set<Appointment>();
    public DbSet<DepositRule>     DepositRules     => Set<DepositRule>();
    public DbSet<Client>          Clients          => Set<Client>();
    public DbSet<ClientProfile>   ClientProfiles   => Set<ClientProfile>();
    public DbSet<TattooRecord>    TattooRecords    => Set<TattooRecord>();
    public DbSet<Artist>          Artists          => Set<Artist>();
    public DbSet<ArtistSchedule>  ArtistSchedules  => Set<ArtistSchedule>();
    public DbSet<ArtistTimeOff>   ArtistTimeOffs   => Set<ArtistTimeOff>();
    public DbSet<PortfolioImage>  PortfolioImages  => Set<PortfolioImage>();
    public DbSet<Design>           Designs           => Set<Design>();
    public DbSet<DesignRevision>   DesignRevisions   => Set<DesignRevision>();
    public DbSet<DesignApproval>   DesignApprovals   => Set<DesignApproval>();
    public DbSet<DesignShareToken> DesignShareTokens => Set<DesignShareToken>();
    public DbSet<Payment>              Payments             => Set<Payment>();
    public DbSet<SessionSplit>         SessionSplits        => Set<SessionSplit>();
    public DbSet<IntakeForm>      IntakeForms      => Set<IntakeForm>();
    public DbSet<ConsentForm>     ConsentForms     => Set<ConsentForm>();
    public DbSet<NotificationLog>               NotificationLogs              => Set<NotificationLog>();
    public DbSet<StudioNotificationPreference>  StudioNotificationPreferences => Set<StudioNotificationPreference>();
    public DbSet<ClientNotificationPreference>  ClientNotificationPreferences => Set<ClientNotificationPreference>();

    // --- Issuer-level (no tenant filter) ---
    public DbSet<Studio>             Studios             => Set<Studio>();
    public DbSet<Plan>               Plans               => Set<Plan>();
    public DbSet<Subscription>       Subscriptions       => Set<Subscription>();
    public DbSet<ReferralCode>       ReferralCodes       => Set<ReferralCode>();
    public DbSet<ReferralRedemption> ReferralRedemptions => Set<ReferralRedemption>();

    // --- Cross-tenant public data (no tenant filter) ---
    public DbSet<Review> Reviews => Set<Review>();

    // --- User-saved images (no tenant filter — user may save from any studio) ---
    public DbSet<SavedPortfolioImage> SavedPortfolioImages => Set<SavedPortfolioImage>();

    // --- Instagram (artist-scoped, no tenant filter — nightly sync job iterates all
    //     tenants; application handlers must verify ArtistId ownership via Artists) ---
    public DbSet<InstagramConnection> InstagramConnections => Set<InstagramConnection>();
    public DbSet<InstagramPost>       InstagramPosts       => Set<InstagramPost>();

    // --- Platform feedback (no tenant filter — issuer reads across all studios) ---
    public DbSet<FeedbackReport> FeedbackReports => Set<FeedbackReport>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        // See UtcDateTimeConverter — keeps API timestamps consistently UTC ('Z')
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<UtcNullableDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        builder.Entity<Appointment>()    .HasQueryFilter(a => a.StudioId == tenant.StudioId && a.DeletedAt == null);
        builder.Entity<DepositRule>()    .HasQueryFilter(d => d.StudioId == tenant.StudioId && d.DeletedAt == null);
        builder.Entity<Client>()         .HasQueryFilter(c => c.StudioId == tenant.StudioId && c.DeletedAt == null);
        builder.Entity<ClientProfile>()  .HasQueryFilter(c => c.StudioId == tenant.StudioId && c.DeletedAt == null);
        builder.Entity<TattooRecord>()   .HasQueryFilter(t => t.StudioId == tenant.StudioId && t.DeletedAt == null);
        builder.Entity<Artist>()         .HasQueryFilter(a => a.StudioId == tenant.StudioId && a.DeletedAt == null);
        builder.Entity<ArtistSchedule>() .HasQueryFilter(s => s.StudioId == tenant.StudioId && s.DeletedAt == null);
        builder.Entity<ArtistTimeOff>()  .HasQueryFilter(t => t.StudioId == tenant.StudioId && t.DeletedAt == null);
        builder.Entity<PortfolioImage>() .HasQueryFilter(p => p.StudioId == tenant.StudioId && p.DeletedAt == null);
        builder.Entity<Design>()         .HasQueryFilter(d => d.StudioId == tenant.StudioId && d.DeletedAt == null);
        builder.Entity<DesignRevision>()   .HasQueryFilter(d => d.StudioId == tenant.StudioId && d.DeletedAt == null);
        builder.Entity<DesignApproval>()   .HasQueryFilter(d => d.StudioId == tenant.StudioId && d.DeletedAt == null);
        builder.Entity<DesignShareToken>() .HasQueryFilter(t => t.StudioId == tenant.StudioId && t.DeletedAt == null);
        builder.Entity<Payment>()             .HasQueryFilter(p => p.StudioId == tenant.StudioId && p.DeletedAt == null);
        builder.Entity<SessionSplit>()        .HasQueryFilter(s => s.StudioId == tenant.StudioId && s.DeletedAt == null);
        builder.Entity<IntakeForm>()     .HasQueryFilter(i => i.StudioId == tenant.StudioId && i.DeletedAt == null);
        builder.Entity<ConsentForm>()    .HasQueryFilter(c => c.StudioId == tenant.StudioId && c.DeletedAt == null);
        builder.Entity<NotificationLog>()              .HasQueryFilter(n => n.StudioId == tenant.StudioId && n.DeletedAt == null);
        builder.Entity<StudioNotificationPreference>() .HasQueryFilter(p => p.StudioId == tenant.StudioId && p.DeletedAt == null);
        // ClientNotificationPreference — NOT filtered, dual-keyed by (UserId, StudioId); see ClientNotificationPreferenceConfiguration.

        builder.Entity<SavedPortfolioImage>(b =>
        {
            b.ToTable("SavedPortfolioImages");
            b.HasKey(s => s.Id);
            b.Property(s => s.SavedAt).IsRequired();
            // One save per user per image — enforced by unique index
            b.HasIndex(s => new { s.UserId, s.PortfolioImageId }).IsUnique();
            b.HasOne(s => s.PortfolioImage)
             .WithMany()
             .HasForeignKey(s => s.PortfolioImageId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Review>(entity =>
        {
            entity.HasKey(r => r.Id);

            entity.Property(r => r.AuthorName).HasMaxLength(200).IsRequired();
            entity.Property(r => r.Body).HasMaxLength(2000).IsRequired();
            entity.Property(r => r.Rating).IsRequired();

            // No HasQueryFilter — reviews are public cross-tenant data.
            entity.HasIndex(r => r.StudioId);
            entity.HasIndex(r => r.ArtistId);
            entity.HasIndex(r => r.PortfolioImageId);
            entity.HasIndex(r => new { r.AuthorUserId, r.StudioId }).IsUnique();
            entity.HasIndex(r => new { r.AuthorUserId, r.ArtistId }).IsUnique();
            entity.HasIndex(r => new { r.AuthorUserId, r.PortfolioImageId }).IsUnique();

            entity.HasOne<PortfolioImage>()
                  .WithMany()
                  .HasForeignKey(r => r.PortfolioImageId)
                  .OnDelete(DeleteBehavior.Cascade)
                  .IsRequired(false);
        });

        builder.Entity<FeedbackReport>(entity =>
        {
            entity.HasKey(r => r.Id);

            entity.Property(r => r.SubmitterRole).HasMaxLength(20).IsRequired();
            entity.Property(r => r.StudioName).HasMaxLength(200).IsRequired();
            entity.Property(r => r.Type).HasConversion<string>().HasMaxLength(30).IsRequired();
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(r => r.Title).HasMaxLength(150).IsRequired();
            entity.Property(r => r.Body).HasMaxLength(2000).IsRequired();
            entity.Property(r => r.IssuerNote).HasMaxLength(1000);

            // No HasQueryFilter — issuer reads across all studios.
            entity.HasIndex(r => r.StudioId);
            entity.HasIndex(r => r.Status);
            entity.HasIndex(r => r.CreatedAt);
        });
    }
}
