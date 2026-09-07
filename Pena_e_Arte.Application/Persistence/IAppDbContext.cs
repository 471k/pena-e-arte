using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Persistence;

public interface IAppDbContext
{
    // Tenant-scoped
    DbSet<Appointment> Appointments { get; }
    DbSet<AppointmentAttachment> AppointmentAttachments { get; }
    DbSet<DepositRule> DepositRules { get; }
    DbSet<Client> Clients { get; }
    DbSet<ClientProfile> ClientProfiles { get; }
    DbSet<TattooRecord> TattooRecords { get; }
    DbSet<Artist> Artists { get; }
    DbSet<ArtistSchedule> ArtistSchedules { get; }
    DbSet<ArtistTimeOff> ArtistTimeOffs { get; }
    DbSet<StudioClosure> StudioClosures { get; }
    DbSet<PortfolioImage> PortfolioImages { get; }
    DbSet<Design> Designs { get; }
    DbSet<DesignRevision> DesignRevisions { get; }
    DbSet<DesignApproval> DesignApprovals { get; }
    DbSet<DesignShareToken> DesignShareTokens { get; }
    DbSet<Payment> Payments { get; }
    DbSet<SessionSplit> SessionSplits { get; }
    DbSet<IntakeForm> IntakeForms { get; }
    DbSet<BookingIntake> BookingIntakes { get; }
    DbSet<ConsentForm> ConsentForms { get; }
    DbSet<ConsentTemplate> ConsentTemplates { get; }
    DbSet<NotificationLog> NotificationLogs { get; }
    DbSet<StudioNotificationPreference> StudioNotificationPreferences { get; }
    DbSet<ClientNotificationPreference> ClientNotificationPreferences { get; }
    DbSet<ManualReminder> ManualReminders { get; }
    DbSet<Conversation> Conversations { get; }
    DbSet<ChatMessage> ChatMessages { get; }

    // Admin-level
    DbSet<Studio> Studios { get; }
    DbSet<Plan> Plans { get; }
    DbSet<PlanPrice> PlanPrices { get; }
    DbSet<Subscription> Subscriptions { get; }
    DbSet<ReferralCode> ReferralCodes { get; }
    DbSet<ReferralRedemption> ReferralRedemptions { get; }

    // Solo-artist studio-join invites — no tenant filter (invited party is not yet a
    // member of the inviting studio's tenant; see AppDbContext)
    DbSet<StudioJoinInvite> StudioJoinInvites { get; }

    // Cross-tenant public data
    DbSet<Review> Reviews { get; }

    // User-saved portfolio images — cross-tenant (user may belong to any studio)
    DbSet<SavedPortfolioImage> SavedPortfolioImages { get; }

    // Instagram — artist-scoped, no tenant filter (see AppDbContext)
    DbSet<InstagramConnection> InstagramConnections { get; }
    DbSet<InstagramPost> InstagramPosts { get; }

    // Social verification — polymorphic subject (Artist or Studio), no tenant filter (see AppDbContext)
    DbSet<SocialAccountLink> SocialAccountLinks { get; }

    // Platform feedback — no tenant filter (admin reads across all studios)
    DbSet<FeedbackReport> FeedbackReports { get; }
    DbSet<FeedbackMessage> FeedbackMessages { get; }

    // Help search analytics — tenant-scoped write, admin reads cross-tenant via IgnoreQueryFilters
    DbSet<HelpSearchLog> HelpSearchLogs { get; }

    // Onboarding tour completion — per-user, cross-tenant, no tenant filter needed
    DbSet<UserOnboardingState> UserOnboardingStates { get; }

    // Structured audit log — no tenant filter (StudioId nullable, platform-wide actions allowed)
    DbSet<AuditLogEntry> AuditLogEntries { get; }
    DbSet<StudioCredentialRef> StudioCredentialRefs { get; }

    // Trust & safety conduct reports — no tenant filter, same non-tenant shape as
    // Review/FeedbackReport/AuditLogEntry (see AppDbContext)
    DbSet<ConductReport> ConductReports { get; }

    // Traffic analytics — no tenant filter (StudioId nullable, admin-only cross-tenant reads)
    DbSet<TrafficEvent> TrafficEvents { get; }
    DbSet<TrafficDailyAggregate> TrafficDailyAggregates { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
