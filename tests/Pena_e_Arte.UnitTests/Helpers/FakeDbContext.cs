using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.UnitTests.Helpers;

public sealed class FakeDbContext(DbContextOptions<FakeDbContext> options)
    : DbContext(options), IAppDbContext
{
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
    public DbSet<Payment>             Payments            => Set<Payment>();
    public DbSet<SessionSplit>        SessionSplits       => Set<SessionSplit>();
    public DbSet<IntakeForm>      IntakeForms      => Set<IntakeForm>();
    public DbSet<ConsentForm>     ConsentForms     => Set<ConsentForm>();
    public DbSet<NotificationLog>               NotificationLogs              => Set<NotificationLog>();
    public DbSet<StudioNotificationPreference>  StudioNotificationPreferences => Set<StudioNotificationPreference>();
    public DbSet<ClientNotificationPreference>  ClientNotificationPreferences => Set<ClientNotificationPreference>();
    public DbSet<Studio>             Studios             => Set<Studio>();
    public DbSet<Plan>               Plans               => Set<Plan>();
    public DbSet<PlanPrice>          PlanPrices          => Set<PlanPrice>();
    public DbSet<Subscription>       Subscriptions       => Set<Subscription>();
    public DbSet<ReferralCode>       ReferralCodes       => Set<ReferralCode>();
    public DbSet<ReferralRedemption> ReferralRedemptions => Set<ReferralRedemption>();
    public DbSet<Review>                Reviews              => Set<Review>();
    public DbSet<SavedPortfolioImage>   SavedPortfolioImages => Set<SavedPortfolioImage>();
    public DbSet<InstagramConnection>   InstagramConnections => Set<InstagramConnection>();
    public DbSet<InstagramPost>         InstagramPosts       => Set<InstagramPost>();
    public DbSet<FeedbackReport>        FeedbackReports      => Set<FeedbackReport>();
    public DbSet<HelpSearchLog>         HelpSearchLogs       => Set<HelpSearchLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Studio>()
            .HasOne(s => s.Subscription)
            .WithOne(sub => sub.Studio)
            .HasForeignKey<Subscription>(sub => sub.StudioId);

        modelBuilder.Entity<DesignRevision>()
            .HasOne(r => r.Approval)
            .WithOne(a => a.DesignRevision)
            .HasForeignKey<DesignApproval>(a => a.DesignRevisionId);

        modelBuilder.Entity<DesignShareToken>()
            .HasOne(t => t.DesignRevision)
            .WithMany()
            .HasForeignKey(t => t.DesignRevisionId);

        modelBuilder.Entity<ClientProfile>()
            .HasOne(cp => cp.Client)
            .WithOne(c => c.Profile)
            .HasForeignKey<ClientProfile>(cp => cp.ClientId);

        modelBuilder.Entity<ClientProfile>()
            .OwnsOne(cp => cp.BodyMap);

        modelBuilder.Entity<ReferralCode>()
            .HasOne(r => r.Studio)
            .WithMany()
            .HasForeignKey(r => r.StudioId);

        modelBuilder.Entity<ReferralCode>()
            .HasMany(r => r.Redemptions)
            .WithOne()
            .HasForeignKey(rr => rr.ReferralCodeId);

        modelBuilder.Entity<PortfolioImage>()
            .HasOne(p => p.Artist)
            .WithMany(a => a.Portfolio)
            .HasForeignKey(p => p.ArtistId);

        modelBuilder.Entity<Review>()
            .HasOne<PortfolioImage>()
            .WithMany()
            .HasForeignKey(r => r.PortfolioImageId)
            .IsRequired(false);

        modelBuilder.Entity<SavedPortfolioImage>()
            .HasOne(s => s.PortfolioImage)
            .WithMany()
            .HasForeignKey(s => s.PortfolioImageId);

        modelBuilder.Entity<InstagramConnection>()
            .HasOne(c => c.Artist)
            .WithMany()
            .HasForeignKey(c => c.ArtistId);

        modelBuilder.Entity<InstagramPost>()
            .HasOne(p => p.Artist)
            .WithMany()
            .HasForeignKey(p => p.ArtistId);
    }

    public static FakeDbContext Create() =>
        new(new DbContextOptionsBuilder<FakeDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
