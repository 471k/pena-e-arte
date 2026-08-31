using System.Text.Json;
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
    public DbSet<Appointment> Appointments => Set<Appointment>();
    public DbSet<AppointmentAttachment> AppointmentAttachments => Set<AppointmentAttachment>();
    public DbSet<DepositRule> DepositRules => Set<DepositRule>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ClientProfile> ClientProfiles => Set<ClientProfile>();
    public DbSet<TattooRecord> TattooRecords => Set<TattooRecord>();
    public DbSet<Artist> Artists => Set<Artist>();
    public DbSet<ArtistSchedule> ArtistSchedules => Set<ArtistSchedule>();
    public DbSet<ArtistTimeOff> ArtistTimeOffs => Set<ArtistTimeOff>();
    public DbSet<StudioClosure> StudioClosures => Set<StudioClosure>();
    public DbSet<PortfolioImage> PortfolioImages => Set<PortfolioImage>();
    public DbSet<Design> Designs => Set<Design>();
    public DbSet<DesignRevision> DesignRevisions => Set<DesignRevision>();
    public DbSet<DesignApproval> DesignApprovals => Set<DesignApproval>();
    public DbSet<DesignShareToken> DesignShareTokens => Set<DesignShareToken>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<SessionSplit> SessionSplits => Set<SessionSplit>();
    public DbSet<IntakeForm> IntakeForms => Set<IntakeForm>();
    public DbSet<BookingIntake> BookingIntakes => Set<BookingIntake>();
    public DbSet<ConsentForm> ConsentForms => Set<ConsentForm>();
    public DbSet<ConsentTemplate> ConsentTemplates => Set<ConsentTemplate>();
    public DbSet<NotificationLog> NotificationLogs => Set<NotificationLog>();
    public DbSet<StudioNotificationPreference> StudioNotificationPreferences => Set<StudioNotificationPreference>();
    public DbSet<ClientNotificationPreference> ClientNotificationPreferences => Set<ClientNotificationPreference>();
    public DbSet<ManualReminder> ManualReminders => Set<ManualReminder>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();

    // --- Issuer-level (no tenant filter) ---
    public DbSet<Studio> Studios => Set<Studio>();
    public DbSet<Plan> Plans => Set<Plan>();
    public DbSet<PlanPrice> PlanPrices => Set<PlanPrice>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<ReferralCode> ReferralCodes => Set<ReferralCode>();
    public DbSet<ReferralRedemption> ReferralRedemptions => Set<ReferralRedemption>();

    // --- Solo-artist studio-join invites (no tenant filter — the invited party is not a
    //     member of the inviting studio's tenant until they accept; handlers resolve and
    //     verify both sides explicitly, see InviteSoloArtistToJoinCommand /
    //     AcceptStudioJoinInviteCommand) ---
    public DbSet<StudioJoinInvite> StudioJoinInvites => Set<StudioJoinInvite>();

    // --- Cross-tenant public data (no tenant filter) ---
    public DbSet<Review> Reviews => Set<Review>();

    // --- User-saved images (no tenant filter — user may save from any studio) ---
    public DbSet<SavedPortfolioImage> SavedPortfolioImages => Set<SavedPortfolioImage>();

    // --- Instagram (artist-scoped, no tenant filter — nightly sync job iterates all
    //     tenants; application handlers must verify ArtistId ownership via Artists) ---
    public DbSet<InstagramConnection> InstagramConnections => Set<InstagramConnection>();
    public DbSet<InstagramPost> InstagramPosts => Set<InstagramPost>();

    // --- Social verification (polymorphic Artist-or-Studio subject, no tenant filter —
    //     same documented exception as InstagramConnection above; handlers must filter
    //     by (SubjectType, SubjectId) and verify tenant ownership explicitly) ---
    public DbSet<SocialAccountLink> SocialAccountLinks => Set<SocialAccountLink>();

    // --- Platform feedback (no tenant filter — issuer reads across all studios) ---
    public DbSet<FeedbackReport> FeedbackReports => Set<FeedbackReport>();
    public DbSet<FeedbackMessage> FeedbackMessages => Set<FeedbackMessage>();

    // --- Help search analytics (tenant-scoped write; issuer aggregate read via IgnoreQueryFilters) ---
    public DbSet<HelpSearchLog> HelpSearchLogs => Set<HelpSearchLog>();

    // --- Onboarding tour completion (no tenant filter — per-user, cross-tenant) ---
    public DbSet<UserOnboardingState> UserOnboardingStates => Set<UserOnboardingState>();

    // --- Structured audit log (no tenant filter — StudioId nullable, platform-wide actions allowed) ---
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<StudioCredentialRef> StudioCredentialRefs => Set<StudioCredentialRef>();

    // --- Trust & safety conduct reports (no tenant filter — same non-tenant shape as
    //     Review/FeedbackReport/AuditLogEntry; target's studio is unrelated to the filing
    //     client's own current tenant) ---
    public DbSet<ConductReport> ConductReports => Set<ConductReport>();

    // --- Traffic analytics (no tenant filter — StudioId nullable, issuer-only cross-tenant reads) ---
    public DbSet<TrafficEvent> TrafficEvents => Set<TrafficEvent>();
    public DbSet<TrafficDailyAggregate> TrafficDailyAggregates => Set<TrafficDailyAggregate>();

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

        builder.Entity<Appointment>().HasQueryFilter(a => a.StudioId == tenant.StudioId && a.DeletedAt == null);
        builder.Entity<AppointmentAttachment>().HasQueryFilter(a => a.StudioId == tenant.StudioId && a.DeletedAt == null);
        builder.Entity<DepositRule>().HasQueryFilter(d => d.StudioId == tenant.StudioId && d.DeletedAt == null);
        builder.Entity<Client>().HasQueryFilter(c => c.StudioId == tenant.StudioId && c.DeletedAt == null);
        builder.Entity<ClientProfile>().HasQueryFilter(c => c.StudioId == tenant.StudioId && c.DeletedAt == null);
        builder.Entity<TattooRecord>().HasQueryFilter(t => t.StudioId == tenant.StudioId && t.DeletedAt == null);
        builder.Entity<Artist>().HasQueryFilter(a => a.StudioId == tenant.StudioId && a.DeletedAt == null);
        builder.Entity<ArtistSchedule>().HasQueryFilter(s => s.StudioId == tenant.StudioId && s.DeletedAt == null);
        builder.Entity<ArtistTimeOff>().HasQueryFilter(t => t.StudioId == tenant.StudioId && t.DeletedAt == null);
        builder.Entity<StudioClosure>().HasQueryFilter(c => c.StudioId == tenant.StudioId && c.DeletedAt == null);
        builder.Entity<PortfolioImage>().HasQueryFilter(p => p.StudioId == tenant.StudioId && p.DeletedAt == null);
        builder.Entity<Design>().HasQueryFilter(d => d.StudioId == tenant.StudioId && d.DeletedAt == null);
        builder.Entity<DesignRevision>().HasQueryFilter(d => d.StudioId == tenant.StudioId && d.DeletedAt == null);
        builder.Entity<DesignApproval>().HasQueryFilter(d => d.StudioId == tenant.StudioId && d.DeletedAt == null);
        builder.Entity<DesignShareToken>().HasQueryFilter(t => t.StudioId == tenant.StudioId && t.DeletedAt == null);
        builder.Entity<Payment>().HasQueryFilter(p => p.StudioId == tenant.StudioId && p.DeletedAt == null);
        builder.Entity<SessionSplit>().HasQueryFilter(s => s.StudioId == tenant.StudioId && s.DeletedAt == null);
        builder.Entity<IntakeForm>().HasQueryFilter(i => i.StudioId == tenant.StudioId && i.DeletedAt == null);
        builder.Entity<BookingIntake>().HasQueryFilter(i => i.StudioId == tenant.StudioId && i.DeletedAt == null);
        builder.Entity<ConsentForm>().HasQueryFilter(c => c.StudioId == tenant.StudioId && c.DeletedAt == null);
        builder.Entity<NotificationLog>().HasQueryFilter(n => n.StudioId == tenant.StudioId && n.DeletedAt == null);
        builder.Entity<ManualReminder>().HasQueryFilter(m => m.StudioId == tenant.StudioId && m.DeletedAt == null);
        builder.Entity<HelpSearchLog>().HasQueryFilter(h => h.StudioId == tenant.StudioId && h.DeletedAt == null);
        builder.Entity<StudioNotificationPreference>().HasQueryFilter(p => p.StudioId == tenant.StudioId && p.DeletedAt == null);
        builder.Entity<Conversation>().HasQueryFilter(c => c.StudioId == tenant.StudioId && c.DeletedAt == null);
        builder.Entity<ChatMessage>().HasQueryFilter(m => m.StudioId == tenant.StudioId && m.DeletedAt == null);
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
            entity.HasIndex(r => r.AppointmentId);
            // Eligibility is per-completed-appointment, not a lifetime cap per author:
            // one studio review and one artist review per appointment (a client can
            // leave both from the same visit — they're different rows/targets — but
            // can't double-submit either one for the same appointment). MySQL treats
            // NULL as distinct in composite unique indexes, so artist-review rows
            // (StudioId null) and portfolio-image rows (AppointmentId null) never
            // collide with these.
            entity.HasIndex(r => new { r.AppointmentId, r.StudioId }).IsUnique();
            entity.HasIndex(r => new { r.AppointmentId, r.ArtistId }).IsUnique();
            entity.HasIndex(r => new { r.AuthorUserId, r.PortfolioImageId }).IsUnique();

            entity.HasOne<PortfolioImage>()
                  .WithMany()
                  .HasForeignKey(r => r.PortfolioImageId)
                  .OnDelete(DeleteBehavior.Cascade)
                  .IsRequired(false);

            // SetNull (not Cascade) — a review is a historical record and should
            // survive even if its appointment is later removed.
            entity.HasOne<Appointment>()
                  .WithMany()
                  .HasForeignKey(r => r.AppointmentId)
                  .OnDelete(DeleteBehavior.SetNull)
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

            entity.Property(r => r.AttachmentUrls)
                  .HasConversion(
                      v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                      v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                  .HasColumnType("json");

            // No HasQueryFilter — issuer reads across all studios.
            entity.HasIndex(r => r.StudioId);
            entity.HasIndex(r => r.Status);
            entity.HasIndex(r => r.CreatedAt);

            entity.HasMany(r => r.Messages)
                  .WithOne()
                  .HasForeignKey(m => m.FeedbackReportId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FeedbackMessage>(entity =>
        {
            entity.HasKey(m => m.Id);

            entity.Property(m => m.AuthorRole).HasMaxLength(20).IsRequired();
            entity.Property(m => m.Body).HasMaxLength(2000).IsRequired();

            // No HasQueryFilter — child of a non-tenant entity, same as FeedbackReport itself.
            entity.HasIndex(m => m.FeedbackReportId);
        });

        builder.Entity<UserOnboardingState>(entity =>
        {
            entity.HasKey(u => u.Id);

            entity.Property(u => u.Role).HasMaxLength(20).IsRequired();

            // No HasQueryFilter — state belongs to the user, not a studio.
            entity.HasIndex(u => new { u.UserId, u.Role }).IsUnique();
        });

        builder.Entity<AuditLogEntry>(entity =>
        {
            entity.HasKey(a => a.Id);

            entity.Property(a => a.ActorRole).HasMaxLength(20).IsRequired();
            entity.Property(a => a.Action).HasMaxLength(100).IsRequired();
            entity.Property(a => a.TargetType).HasMaxLength(50).IsRequired();
            entity.Property(a => a.Metadata).HasColumnType("json").IsRequired();

            // No HasQueryFilter — deliberate deviation from the standard TenantEntity shape.
            // StudioId is nullable (null = platform-wide action); "who can read which rows" is
            // enforced in the query handlers (GetAuditLogHandler / GetMyStudioAuditLogHandler),
            // not here. Same non-tenant-scoped shape as FeedbackReport/UserOnboardingState above.
            entity.HasIndex(a => a.Action);
            entity.HasIndex(a => a.CreatedAt);
            entity.HasIndex(a => a.StudioId);
        });

        builder.Entity<StudioCredentialRef>(entity =>
        {
            entity.Property(c => c.Provider).HasConversion<string>().HasMaxLength(20).IsRequired();
            entity.Property(c => c.SecretPath).HasMaxLength(400).IsRequired();
            // Tenant-scoped config: one pointer per (studio, provider).
            entity.HasIndex(c => new { c.StudioId, c.Provider }).IsUnique();
        });
        builder.Entity<StudioCredentialRef>()
            .HasQueryFilter(c => c.StudioId == tenant.StudioId && c.DeletedAt == null);

        builder.Entity<ConsentTemplate>(entity =>
        {
            entity.HasKey(t => t.Id);
            entity.Property(t => t.Kind).HasConversion<string>().HasMaxLength(40).IsRequired();
            entity.Property(t => t.Version).HasMaxLength(50).IsRequired();
            entity.Property(t => t.BodyText).IsRequired();

            // No HasQueryFilter — deliberate, same rationale as AuditLogEntry above.
            // StudioId is nullable (null = platform-default template); the active template
            // for a studio is resolved explicitly in the handlers via ConsentTemplateResolver,
            // narrowing to `StudioId == tenant.StudioId || StudioId == null`, never a filter.
            entity.HasIndex(t => new { t.StudioId, t.Kind, t.IsActive });
        });

        builder.Entity<ConductReport>(entity =>
        {
            entity.ToTable("ConductReports");
            entity.HasKey(r => r.Id);

            entity.Property(r => r.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(r => r.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
            entity.Property(r => r.Reason).HasMaxLength(2000).IsRequired();
            entity.Property(r => r.ReporterName).HasMaxLength(200).IsRequired();
            entity.Property(r => r.ResolutionNote).HasMaxLength(2000);

            // Same JSON column conversion as FeedbackReport.AttachmentUrls above.
            entity.Property(r => r.AttachmentUrls)
                  .HasConversion(
                      v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                      v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
                  .HasColumnType("json");

            // No HasQueryFilter — deliberate, same rationale as AuditLogEntry above; the
            // target's studio is unrelated to the filing client's own current tenant.
            entity.HasIndex(r => r.StudioId);
            entity.HasIndex(r => r.ArtistId);
            entity.HasIndex(r => r.Status);
        });
    }
}
