using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Application.Persistence;

public interface IAppDbContext
{
    // Tenant-scoped
    DbSet<Appointment>     Appointments     { get; }
    DbSet<DepositRule>     DepositRules     { get; }
    DbSet<Client>          Clients          { get; }
    DbSet<ClientProfile>   ClientProfiles   { get; }
    DbSet<TattooRecord>    TattooRecords    { get; }
    DbSet<Artist>          Artists          { get; }
    DbSet<Design>            Designs            { get; }
    DbSet<DesignRevision>    DesignRevisions    { get; }
    DbSet<DesignApproval>    DesignApprovals    { get; }
    DbSet<DesignShareToken>  DesignShareTokens  { get; }
    DbSet<Payment>             Payments            { get; }
    DbSet<SessionSplit>        SessionSplits       { get; }
    DbSet<IntakeForm>      IntakeForms      { get; }
    DbSet<ConsentForm>     ConsentForms     { get; }
    DbSet<NotificationLog>               NotificationLogs               { get; }
    DbSet<StudioNotificationPreference>  StudioNotificationPreferences  { get; }

    // Issuer-level
    DbSet<Studio>              Studios             { get; }
    DbSet<Plan>                Plans               { get; }
    DbSet<Subscription>        Subscriptions       { get; }
    DbSet<ReferralCode>        ReferralCodes       { get; }
    DbSet<ReferralRedemption>  ReferralRedemptions { get; }

    // Cross-tenant public data
    DbSet<Review> Reviews { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
