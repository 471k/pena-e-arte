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

    // --- Issuer-level (no tenant filter) ---
    public DbSet<Studio>             Studios             => Set<Studio>();
    public DbSet<Plan>               Plans               => Set<Plan>();
    public DbSet<Subscription>       Subscriptions       => Set<Subscription>();
    public DbSet<ReferralCode>       ReferralCodes       => Set<ReferralCode>();
    public DbSet<ReferralRedemption> ReferralRedemptions => Set<ReferralRedemption>();

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
    }
}
