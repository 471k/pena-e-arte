using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.ValueObjects;

namespace Pena_e_Arte.Infrastructure.Persistence.Seed;

public static class DataSeeder
{
    private const string Password = "Password123";

    // ─── Issuer-level IDs ──────────────────────────────────────────────────────

    internal static readonly Guid StarterPlanId = new("aaaa0001-0000-0000-0000-000000000000");
    internal static readonly Guid GrowthPlanId = new("aaaa0002-0000-0000-0000-000000000000");
    internal static readonly Guid ProPlanId = new("aaaa0003-0000-0000-0000-000000000000");
    internal static readonly Guid PremiumPlanId = new("aaaa0004-0000-0000-0000-000000000000");
    internal static readonly Guid FreePlanId = new("aaaa0006-0000-0000-0000-000000000000");

    private static readonly Guid Studio1Id = new("bbbb0001-0000-0000-0000-000000000000");
    private static readonly Guid Studio2Id = new("bbbb0002-0000-0000-0000-000000000000");
    private static readonly Guid Subscription1Id = new("cccc0001-0000-0000-0000-000000000000");
    private static readonly Guid Subscription2Id = new("cccc0002-0000-0000-0000-000000000000");

    // ─── Studio 1 User IDs (strings — IdentityUser.Id) ────────────────────────

    private static readonly string IssuerUserId = "dddd0000-0000-0000-0000-000000000000";
    private static readonly string S1OwnerUserId = "dddd0001-0000-0000-0000-000000000001";
    private static readonly string S1Artist1UserId = "dddd0001-0000-0000-0000-000000000002";
    private static readonly string S1Artist2UserId = "dddd0001-0000-0000-0000-000000000003";
    private static readonly string S1Artist3UserId = "dddd0001-0000-0000-0000-000000000004";

    // Single source of truth for Sofia's login email — the Identity user and the Artist
    // record MUST share it, or FindByEmailAsync fails and login silently breaks.
    private const string S1Artist3Email = "sofia1.alves@ink-soul.test";
    private static readonly string S1Client1UserId = "dddd0001-0000-0000-0000-000000000005";
    private static readonly string S1Client2UserId = "dddd0001-0000-0000-0000-000000000006";
    private static readonly string S1Client3UserId = "dddd0001-0000-0000-0000-000000000007";
    private static readonly string S1Client4UserId = "dddd0001-0000-0000-0000-000000000008";
    private static readonly string S1Client5UserId = "dddd0001-0000-0000-0000-000000000009";

    // ─── Studio 2 User IDs ────────────────────────────────────────────────────

    private static readonly string S2OwnerUserId = "dddd0002-0000-0000-0000-000000000001";
    private static readonly string S2Artist1UserId = "dddd0002-0000-0000-0000-000000000002";
    private static readonly string S2Client1UserId = "dddd0002-0000-0000-0000-000000000003";
    private static readonly string S2Client2UserId = "dddd0002-0000-0000-0000-000000000004";

    // ─── Studio 1 Domain Entity IDs ───────────────────────────────────────────

    private static readonly Guid S1Artist1Id = new("eeee0001-0001-0000-0000-000000000000");
    private static readonly Guid S1Artist2Id = new("eeee0001-0002-0000-0000-000000000000");
    private static readonly Guid S1Artist3Id = new("eeee0001-0003-0000-0000-000000000000");

    private static readonly Guid S1Client1Id = new("eeee0001-0011-0000-0000-000000000000");
    private static readonly Guid S1Client2Id = new("eeee0001-0012-0000-0000-000000000000");
    private static readonly Guid S1Client3Id = new("eeee0001-0013-0000-0000-000000000000");
    private static readonly Guid S1Client4Id = new("eeee0001-0014-0000-0000-000000000000");
    private static readonly Guid S1Client5Id = new("eeee0001-0015-0000-0000-000000000000");

    private static readonly Guid S1DepositRule1Id = new("eeee0001-0021-0000-0000-000000000000");
    private static readonly Guid S1DepositRule2Id = new("eeee0001-0022-0000-0000-000000000000");

    private static readonly Guid S1Appt1Id = new("eeee0001-0101-0000-0000-000000000000");
    private static readonly Guid S1Appt2Id = new("eeee0001-0102-0000-0000-000000000000");
    private static readonly Guid S1Appt3Id = new("eeee0001-0103-0000-0000-000000000000");
    private static readonly Guid S1Appt4Id = new("eeee0001-0104-0000-0000-000000000000");
    private static readonly Guid S1Appt5Id = new("eeee0001-0105-0000-0000-000000000000");
    private static readonly Guid S1Appt6Id = new("eeee0001-0106-0000-0000-000000000000");
    private static readonly Guid S1Appt7Id = new("eeee0001-0107-0000-0000-000000000000");
    private static readonly Guid S1Appt8Id = new("eeee0001-0108-0000-0000-000000000000");
    private static readonly Guid S1Appt9Id = new("eeee0001-0109-0000-0000-000000000000");
    private static readonly Guid S1Appt10Id = new("eeee0001-0110-0000-0000-000000000000");
    private static readonly Guid S1Appt11Id = new("eeee0001-0111-0000-0000-000000000000");
    private static readonly Guid S1Appt12Id = new("eeee0001-0112-0000-0000-000000000000");
    private static readonly Guid S1Appt13Id = new("eeee0001-0113-0000-0000-000000000000");
    private static readonly Guid S1Appt14Id = new("eeee0001-0114-0000-0000-000000000000");
    private static readonly Guid S1Appt15Id = new("eeee0001-0115-0000-0000-000000000000");

    private static readonly Guid S1Payment6Id = new("eeee0001-0206-0000-0000-000000000000");
    private static readonly Guid S1Payment7Id = new("eeee0001-0207-0000-0000-000000000000");
    private static readonly Guid S1Payment8Id = new("eeee0001-0208-0000-0000-000000000000");
    private static readonly Guid S1Payment9Id = new("eeee0001-0209-0000-0000-000000000000");
    private static readonly Guid S1Payment10Id = new("eeee0001-0210-0000-0000-000000000000");
    private static readonly Guid S1Payment11Id = new("eeee0001-0211-0000-0000-000000000000");

    private static readonly Guid S1Design1Id = new("eeee0001-0301-0000-0000-000000000000");
    private static readonly Guid S1Design2Id = new("eeee0001-0302-0000-0000-000000000000");
    private static readonly Guid S1Design3Id = new("eeee0001-0303-0000-0000-000000000000");

    // ─── Studio 2 Domain Entity IDs ───────────────────────────────────────────

    private static readonly Guid S2Artist1Id = new("eeee0002-0001-0000-0000-000000000000");

    private static readonly Guid S2Client1Id = new("eeee0002-0011-0000-0000-000000000000");
    private static readonly Guid S2Client2Id = new("eeee0002-0012-0000-0000-000000000000");

    private static readonly Guid S2DepositRule1Id = new("eeee0002-0021-0000-0000-000000000000");

    private static readonly Guid S2Appt1Id = new("eeee0002-0101-0000-0000-000000000000");
    private static readonly Guid S2Appt2Id = new("eeee0002-0102-0000-0000-000000000000");
    private static readonly Guid S2Appt3Id = new("eeee0002-0103-0000-0000-000000000000");
    private static readonly Guid S2Appt4Id = new("eeee0002-0104-0000-0000-000000000000");
    private static readonly Guid S2Appt5Id = new("eeee0002-0105-0000-0000-000000000000");

    private static readonly Guid S2Payment3Id = new("eeee0002-0203-0000-0000-000000000000");
    private static readonly Guid S2Payment4Id = new("eeee0002-0204-0000-0000-000000000000");

    private static readonly Guid S2Design1Id = new("eeee0002-0301-0000-0000-000000000000");

    // ─── Entry point ──────────────────────────────────────────────────────────

    public static async Task SeedAsync(IServiceProvider services)
    {
        using IServiceScope scope = services.CreateScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        UserManager<IdentityUser> userManager =
            scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();

        // Always run: ensure seed credentials + artist slugs are correct
        await EnsureSeedUsersAsync(userManager);
        await EnsureArtistSlugsAsync(db);

        // Always run: platform-default consent templates are system-defined (like the core
        // plan tiers), not demo data — a studio with no custom template falls back to these.
        await EnsurePlatformConsentTemplatesAsync(db);

        // Snapshot BEFORE reconciling. ReconcileCoreTiersAsync will insert StarterPlanId
        // if it's missing, which would make a post-reconcile check always true and
        // silently skip demo-entity seeding on a genuinely fresh database.
        bool coreEntitiesAlreadySeeded = await db.Plans.AnyAsync(p => p.Id == StarterPlanId);

        // Always run: Starter/Growth/Premium/Pro/Free are system-defined tiers, not
        // issuer-owned data — their canonical values (and their PlanPrice rows) live in
        // source control, not the database. Keyed on tier Name + (PlanId, Interval), so
        // an orphan row under a non-canonical Id cannot occur by construction. See
        // architecture.md Decisions Log — "Plan/PlanPrice split".
        await ReconcileCoreTiersAsync(db);

        // Guard: demo studios/subscriptions/appointments/designs/etc. still seed only
        // once — unlike the five canonical plans, this fake data has no "correct"
        // canonical state to reconcile toward on every boot.
        if (coreEntitiesAlreadySeeded)
            return;

        await SeedStudiosAndSubscriptionsAsync(db);
        await SeedStudio1EntitiesAsync(db);
        await SeedStudio2EntitiesAsync(db);
    }

    // ─── Consent templates ──────────────────────────────────────────────────────

    private static async Task EnsurePlatformConsentTemplatesAsync(AppDbContext db)
    {
        bool hasAppointment = await db.ConsentTemplates.AnyAsync(t =>
            t.StudioId == null
            && t.Kind == ConsentTemplateKind.AppointmentConsent
            && t.IsActive);

        if (!hasAppointment)
        {
            db.ConsentTemplates.Add(new ConsentTemplate
            {
                StudioId = null,
                Kind = ConsentTemplateKind.AppointmentConsent,
                Version = "1.0",
                IsActive = true,
                EffectiveFrom = DateTime.UtcNow,
                BodyText =
                    "I confirm that I am of legal age and freely consent to being tattooed. "
                    + "I understand that tattooing involves breaking the skin and carries risks "
                    + "including infection, allergic reaction, and scarring. I confirm the "
                    + "information I have provided about my health, medication, and allergies is "
                    + "accurate. I understand a tattoo is permanent and aftercare is my "
                    + "responsibility.",
            });
        }

        bool hasSharing = await db.ConsentTemplates.AnyAsync(t =>
            t.StudioId == null
            && t.Kind == ConsentTemplateKind.CrossTenantProfileSharing
            && t.IsActive);

        if (!hasSharing)
        {
            db.ConsentTemplates.Add(new ConsentTemplate
            {
                StudioId = null,
                Kind = ConsentTemplateKind.CrossTenantProfileSharing,
                Version = "1.0",
                IsActive = true,
                EffectiveFrom = DateTime.UtcNow,
                BodyText =
                    "I consent to sharing my tattoo history — body-map locations, tattoo photos, "
                    + "and descriptions — with other studios on TattooOS so any artist can view it "
                    + "before a session. My medical notes, allergies, contact details, and payment "
                    + "history are not shared. I can withdraw this consent at any time.",
            });
        }

        await db.SaveChangesAsync();
    }

    // ─── Plans ────────────────────────────────────────────────────────────────

    private sealed record TierPrice(BillingInterval Interval, decimal Price);

    private sealed record CoreTier(
        Guid Id, string Name, int YearlyDiscountPercent, bool AllowBrandingRemoval,
        bool AllowApiAccess, bool PrioritySupport,
        int? MaxArtists, int? MaxAppointmentsPerMonth, int? MaxNotificationsPerMonth,
        int? MaxStorageGb, int? MaxLocations, TierPrice[] Prices);

    // ─── Core tiers (always reconciled) ─────────────────────────────────────────
    //
    // Replaces ReconcileCorePlansAsync + RetireOrphanedNamedPlansAsync (see
    // architecture.md Decisions Log — "Plan/PlanPrice split"). Keyed on tier Name, not a
    // fixed Plan.Id list, and on (PlanId, Interval) for prices — a reconciler with this
    // shape cannot produce the "orphan row under an unrecognized Id" bug class the prior
    // two fixes had to clean up after, by construction: there is nowhere for a second row
    // with the same Name to hide.
    //
    // Practical consequence, spelled out because it's a real behavior change: if an
    // issuer edits Starter, Growth, Premium, Pro, or Free in place via
    // PlanManagementPage, that edit will be reverted back to these values on the next
    // app restart/deploy. That's the intended trade-off — see architecture.md Decisions
    // Log, "Core plan reconciliation replaces one-time plan seed", for the reasoning and
    // for what an issuer should do instead (clone a new Plan row rather than editing one
    // of these five).
    internal static async Task ReconcileCoreTiersAsync(IAppDbContext db)
    {
        CoreTier[] tiers =
        [
            new CoreTier(FreePlanId, "Free", 0, false, false, false,
                1, 15, 50, 1, 1,
                [new TierPrice(BillingInterval.Monthly, 0m)]),
            new CoreTier(StarterPlanId, "Starter", 17, false, false, false,
                1, 40, 150, 2, 1,
                [new TierPrice(BillingInterval.Monthly, 29m)]),
            new CoreTier(GrowthPlanId, "Growth", 17, true, false, false,
                3, 150, 600, 10, 1,
                [new TierPrice(BillingInterval.Monthly, 59m)]),
            new CoreTier(PremiumPlanId, "Premium", 17, true, false, true,
                6, 400, 1200, 25, 2,
                [new TierPrice(BillingInterval.Monthly, 79m), new TierPrice(BillingInterval.Yearly, 790m)]),
            // Soft caps, not true unlimited — protects against a single runaway account
            // inflating Twilio/Hangfire/DB load (owner decision, 2026-07-18).
            new CoreTier(ProPlanId, "Pro", 17, true, true, true,
                10, 1000, 2500, 50, 10,
                [new TierPrice(BillingInterval.Monthly, 99m)]),
        ];

        foreach (CoreTier tier in tiers)
        {
            Plan? plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == tier.Id);
            if (plan is null)
            {
                plan = new Plan { Id = tier.Id };
                db.Plans.Add(plan);
            }

            plan.Name = tier.Name;
            plan.YearlyDiscountPercent = tier.YearlyDiscountPercent;
            plan.AllowBrandingRemoval = tier.AllowBrandingRemoval;
            plan.AllowApiAccess = tier.AllowApiAccess;
            plan.PrioritySupport = tier.PrioritySupport;
            plan.MaxArtists = tier.MaxArtists;
            plan.MaxAppointmentsPerMonth = tier.MaxAppointmentsPerMonth;
            plan.MaxNotificationsPerMonth = tier.MaxNotificationsPerMonth;
            plan.MaxStorageGb = tier.MaxStorageGb;
            plan.MaxLocations = tier.MaxLocations;

            foreach (TierPrice tp in tier.Prices)
            {
                PlanPrice? price = await db.PlanPrices
                    .FirstOrDefaultAsync(pp => pp.PlanId == tier.Id && pp.Interval == tp.Interval);

                if (price is null)
                {
                    db.PlanPrices.Add(new PlanPrice
                    {
                        PlanId = tier.Id,
                        Interval = tp.Interval,
                        Price = tp.Price,
                        // StripePriceId intentionally left null — populated by
                        // StripeDemoSeeder or an issuer, never reconciled here (matches
                        // the established precedent from the pre-PlanPrice reconciler).
                    });
                }
                else
                {
                    price.Price = tp.Price; // reconcile price only — StripePriceId untouched
                }
            }
        }

        await db.SaveChangesAsync();
    }

    // ─── Studios + Subscriptions ──────────────────────────────────────────────

    private static async Task SeedStudiosAndSubscriptionsAsync(AppDbContext db)
    {
        DateTime now = DateTime.UtcNow;

        // Studio 1: active, on Growth plan
        db.Studios.Add(new Studio
        {
            Id = Studio1Id,
            Name = "Ink & Soul Studio",
            Slug = "ink-soul-studio",
            City = "Lisbon",
            OwnerEmail = "owner@ink-soul.test",
            Latitude = 38.7169,
            Longitude = -9.1399,
            IsActive = true,
            TrialExpiresAt = now.AddDays(-16),
            CreatedAt = now.AddDays(-30)
        });

        db.Subscriptions.Add(new Subscription
        {
            Id = Subscription1Id,
            StudioId = Studio1Id,
            PlanId = GrowthPlanId,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Active,
            TrialExpiresAt = null,
            CurrentPeriodEnd = now.AddDays(14),
            GracePeriodEnd = now.AddDays(21),
            // Null = cash-billed semantics: there is no real Stripe subscription behind
            // seed data, and a fake id breaks plan switching / webhook reconciliation.
            StripeSubscriptionId = null
        });

        // Studio 2: trialing, 10 days left
        db.Studios.Add(new Studio
        {
            Id = Studio2Id,
            Name = "Dark Canvas Tattoo",
            Slug = "dark-canvas-tattoo",
            City = "Porto",
            OwnerEmail = "owner@dark-canvas.test",
            Latitude = 41.1579,
            Longitude = -8.6291,
            IsActive = true,
            TrialExpiresAt = now.AddDays(10),
            CreatedAt = now.AddDays(-4)
        });

        db.Subscriptions.Add(new Subscription
        {
            Id = Subscription2Id,
            StudioId = Studio2Id,
            PlanId = StarterPlanId,
            BillingInterval = BillingInterval.Monthly,
            Status = SubscriptionStatus.Trialing,
            TrialExpiresAt = now.AddDays(10),
            CurrentPeriodEnd = now.AddDays(10),
            GracePeriodEnd = now.AddDays(17)
        });

        await db.SaveChangesAsync();
    }

    // ─── Always-run: sync seed users + artist slugs ──────────────────────────

    private static async Task EnsureSeedUsersAsync(UserManager<IdentityUser> userManager)
    {
        await EnsureUserAsync(userManager, IssuerUserId,
            "issuer@pena-arte.test", "issuer", Studio1Id, "Gabriel");
        await EnsureUserAsync(userManager, S1OwnerUserId,
            "owner@ink-soul.test", "owner", Studio1Id, "Inês");
        await EnsureUserAsync(userManager, S1Artist1UserId,
            "elena.martins@ink-soul.test", "artist", Studio1Id, "Elena");
        await EnsureUserAsync(userManager, S1Artist2UserId,
            "marco.santos@ink-soul.test", "artist", Studio1Id, "Marco");
        await EnsureUserAsync(userManager, S1Artist3UserId,
            S1Artist3Email, "artist", Studio1Id, "Sofia");
        await EnsureUserAsync(userManager, S1Client1UserId,
            "ana.costa@client.test", "client", Studio1Id, "Ana");
        await EnsureUserAsync(userManager, S1Client2UserId,
            "pedro.oliveira@client.test", "client", Studio1Id, "Pedro");
        await EnsureUserAsync(userManager, S1Client3UserId,
            "julia.ferreira@client.test", "client", Studio1Id, "Júlia");
        await EnsureUserAsync(userManager, S1Client4UserId,
            "rafael.mendes@client.test", "client", Studio1Id, "Rafael");
        await EnsureUserAsync(userManager, S1Client5UserId,
            "mia.carvalho@client.test", "client", Studio1Id, "Mia");
        await EnsureUserAsync(userManager, S2OwnerUserId,
            "owner@dark-canvas.test", "owner", Studio2Id, "Carlos");
        await EnsureUserAsync(userManager, S2Artist1UserId,
            "luis.rodrigues@dark-canvas.test", "artist", Studio2Id, "Luís");
        await EnsureUserAsync(userManager, S2Client1UserId,
            "sara.lima@dark-canvas.test", "client", Studio2Id, "Sara");
        await EnsureUserAsync(userManager, S2Client2UserId,
            "tomas.gomes@dark-canvas.test", "client", Studio2Id, "Tomás");
    }

    private static async Task EnsureArtistSlugsAsync(AppDbContext db)
    {
        Dictionary<Guid, string> assignments = new()
        {
            [S1Artist1Id] = "elena-martins",
            [S1Artist2Id] = "marco-santos",
            [S1Artist3Id] = "sofia-alves",
            [S2Artist1Id] = "luis-rodrigues",
        };

        // Approved: public portfolio query — see architecture.md AllowAnonymous Exceptions.
        // Uses ExecuteUpdateAsync (bulk UPDATE) instead of load-track-save so that:
        //   1. No EF Core change-tracker involvement — avoids snapshot/converter edge-cases.
        //   2. Matches both NULL and '' slugs — the migration default left some rows as ''.
        foreach ((Guid id, string slug) in assignments)
        {
            await db.Artists
                .IgnoreQueryFilters()
                .Where(a => a.Id == id && (a.Slug == null || a.Slug == string.Empty))
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Slug, slug));
        }
    }

    private static async Task SeedStudio1EntitiesAsync(AppDbContext db)
    {
        DateTime now = DateTime.UtcNow;
        DateTime today = now.Date;

        // ── Artists ──────────────────────────────────────────────────────────

        Artist s1a1 = new()
        {
            Id = S1Artist1Id,
            StudioId = Studio1Id,
            UserId = Guid.Parse(S1Artist1UserId),
            FirstName = "Elena",
            LastName = "Martins",
            Email = "elena.martins@ink-soul.test",
            Specializations = "Traditional,Japanese,Neo-Traditional",
            HourlyRate = 100m,
            UpdatedAt = now
        };
        s1a1.SetSlug("elena-martins");
        Artist s1a2 = new()
        {
            Id = S1Artist2Id,
            StudioId = Studio1Id,
            UserId = Guid.Parse(S1Artist2UserId),
            FirstName = "Marco",
            LastName = "Santos",
            Email = "marco.santos@ink-soul.test",
            Specializations = "Realism,Portraits,Black & Grey",
            HourlyRate = 120m,
            UpdatedAt = now
        };
        s1a2.SetSlug("marco-santos");
        Artist s1a3 = new()
        {
            Id = S1Artist3Id,
            StudioId = Studio1Id,
            UserId = Guid.Parse(S1Artist3UserId),
            FirstName = "Sofia",
            LastName = "Alves",
            Email = S1Artist3Email,
            Specializations = "Geometric,Minimalist,Fine Line",
            HourlyRate = 90m,
            UpdatedAt = now
        };
        s1a3.SetSlug("sofia-alves");

        db.Artists.AddRange(s1a1, s1a2, s1a3);

        // ── Portfolio images ─────────────────────────────────────────────────
        // Real, resolvable placeholder photos (picsum.photos with fixed seeds, so
        // the same "image" is returned on every reseed) — not r2.example.com like
        // the rest of this file's asset URLs, since these specifically back the
        // public Discover feed grid and need to actually render for it to be
        // demoable rather than showing the broken-image fallback everywhere.
        db.PortfolioImages.AddRange(
            new PortfolioImage { StudioId = Studio1Id, ArtistId = s1a1.Id, Style = TattooStyle.Traditional, Category = PortfolioImageCategory.FreshTattoo, ImageUrl = "https://picsum.photos/seed/pena-elena-1/800/1000" },
            new PortfolioImage { StudioId = Studio1Id, ArtistId = s1a1.Id, Style = TattooStyle.Japanese, Category = PortfolioImageCategory.HealedTattoo, ImageUrl = "https://picsum.photos/seed/pena-elena-2/800/1000" },
            new PortfolioImage { StudioId = Studio1Id, ArtistId = s1a2.Id, Style = TattooStyle.Realism, Category = PortfolioImageCategory.FreshTattoo, ImageUrl = "https://picsum.photos/seed/pena-marco-1/800/1000" },
            new PortfolioImage { StudioId = Studio1Id, ArtistId = s1a2.Id, Style = TattooStyle.Realism, Category = PortfolioImageCategory.Design, ImageUrl = "https://picsum.photos/seed/pena-marco-2/800/1000" },
            new PortfolioImage { StudioId = Studio1Id, ArtistId = s1a3.Id, Style = TattooStyle.Geometric, Category = PortfolioImageCategory.HealedTattoo, ImageUrl = "https://picsum.photos/seed/pena-sofia-1/800/1000" },
            new PortfolioImage { StudioId = Studio1Id, ArtistId = s1a3.Id, Style = TattooStyle.Fineline, Category = null, ImageUrl = "https://picsum.photos/seed/pena-sofia-2/800/1000" });

        // ── Clients ───────────────────────────────────────────────────────────

        Client s1c1 = new()
        {
            Id = S1Client1Id,
            StudioId = Studio1Id,
            UserId = Guid.Parse(S1Client1UserId),
            FirstName = "Ana",
            LastName = "Costa",
            Email = "ana.costa@client.test",
            Phone = "+351912345001",
            UpdatedAt = now
        };
        Client s1c2 = new()
        {
            Id = S1Client2Id,
            StudioId = Studio1Id,
            UserId = Guid.Parse(S1Client2UserId),
            FirstName = "Pedro",
            LastName = "Oliveira",
            Email = "pedro.oliveira@client.test",
            Phone = "+351912345002",
            UpdatedAt = now
        };
        Client s1c3 = new()
        {
            Id = S1Client3Id,
            StudioId = Studio1Id,
            UserId = Guid.Parse(S1Client3UserId),
            FirstName = "Júlia",
            LastName = "Ferreira",
            Email = "julia.ferreira@client.test",
            Phone = "+351912345003",
            UpdatedAt = now
        };
        Client s1c4 = new()
        {
            Id = S1Client4Id,
            StudioId = Studio1Id,
            UserId = Guid.Parse(S1Client4UserId),
            FirstName = "Rafael",
            LastName = "Mendes",
            Email = "rafael.mendes@client.test",
            Phone = "+351912345004",
            UpdatedAt = now
        };
        Client s1c5 = new()
        {
            Id = S1Client5Id,
            StudioId = Studio1Id,
            UserId = Guid.Parse(S1Client5UserId),
            FirstName = "Mia",
            LastName = "Carvalho",
            Email = "mia.carvalho@client.test",
            Phone = "+351912345005",
            UpdatedAt = now
        };

        db.Clients.AddRange(s1c1, s1c2, s1c3, s1c4, s1c5);

        // ── Client Profiles ───────────────────────────────────────────────────

        db.ClientProfiles.AddRange(
            new ClientProfile
            {
                StudioId = Studio1Id,
                ClientId = S1Client1Id,
                DateOfBirth = new DateOnly(1992, 3, 15),
                MedicalNotes = "No known conditions.",
                Allergies = "None",
                BodyMap = new BodyMap { Locations = ["left-upper-arm", "right-forearm", "upper-back"] },
                UpdatedAt = now
            },
            new ClientProfile
            {
                StudioId = Studio1Id,
                ClientId = S1Client2Id,
                DateOfBirth = new DateOnly(1988, 7, 22),
                MedicalNotes = "Mild eczema — avoid fragranced aftercare products.",
                Allergies = "Fragrance",
                BodyMap = new BodyMap { Locations = ["left-calf", "chest"] },
                UpdatedAt = now
            },
            new ClientProfile
            {
                StudioId = Studio1Id,
                ClientId = S1Client3Id,
                DateOfBirth = new DateOnly(1995, 11, 5),
                MedicalNotes = "None",
                Allergies = "None",
                BodyMap = new BodyMap { Locations = ["right-thigh", "left-shoulder"] },
                UpdatedAt = now
            },
            new ClientProfile
            {
                StudioId = Studio1Id,
                ClientId = S1Client4Id,
                DateOfBirth = new DateOnly(1990, 4, 30),
                MedicalNotes = "Blood thinner medication — consult before session.",
                Allergies = "Latex",
                BodyMap = new BodyMap { Locations = ["right-upper-arm"] },
                UpdatedAt = now
            },
            new ClientProfile
            {
                StudioId = Studio1Id,
                ClientId = S1Client5Id,
                DateOfBirth = new DateOnly(1998, 9, 12),
                MedicalNotes = "None",
                Allergies = "None",
                BodyMap = new BodyMap { Locations = ["ankle", "wrist"] },
                UpdatedAt = now
            }
        );

        // ── Deposit Rules ─────────────────────────────────────────────────────

        db.DepositRules.AddRange(
            new DepositRule
            {
                Id = S1DepositRule1Id,
                StudioId = Studio1Id,
                Name = "Standard Deposit (20%)",
                AmountFixed = null,
                AmountPercent = 20m,
                // Inactive: percent rules resolve to €0 deposits until session price is
                // tracked, and CreateAppointment picks an arbitrary active rule — keep
                // the fixed rule as the only active one so seeded bookings get deposits.
                IsActive = false,
                UpdatedAt = now
            },
            new DepositRule
            {
                Id = S1DepositRule2Id,
                StudioId = Studio1Id,
                Name = "New Client Fixed (€50)",
                AmountFixed = 50m,
                AmountPercent = null,
                IsActive = true,
                UpdatedAt = now
            }
        );

        await db.SaveChangesAsync();

        // ── Appointments ──────────────────────────────────────────────────────

        // Future appointments — various statuses
        db.Appointments.AddRange(
            // 1. Pending + DepositPending — Elena + Ana, 5 days out
            new Appointment
            {
                Id = S1Appt1Id,
                StudioId = Studio1Id,
                ArtistId = S1Artist1Id,
                ClientId = S1Client1Id,
                Date = today.AddDays(5).AddHours(10),
                EndDate = today.AddDays(5).AddHours(13),
                DurationMinutes = 180,
                Status = AppointmentStatus.Pending,
                DepositStatus = DepositStatus.Pending,
                DepositAmount = 0m,
                Notes = "Large Japanese sleeve piece, first session consultation.",
                UpdatedAt = now
            },
            // 2. Confirmed + DepositPaid — Elena + Pedro, 10 days out
            new Appointment
            {
                Id = S1Appt2Id,
                StudioId = Studio1Id,
                ArtistId = S1Artist1Id,
                ClientId = S1Client2Id,
                Date = today.AddDays(10).AddHours(14),
                EndDate = today.AddDays(10).AddHours(18),
                DurationMinutes = 240,
                Status = AppointmentStatus.Confirmed,
                DepositStatus = DepositStatus.Paid,
                DepositAmount = 50m,
                Notes = "Neo-traditional rose on calf — reference images shared.",
                UpdatedAt = now
            },
            // 3. Confirmed + DepositPending — Marco + Ana, 3 days out
            new Appointment
            {
                Id = S1Appt3Id,
                StudioId = Studio1Id,
                ArtistId = S1Artist2Id,
                ClientId = S1Client1Id,
                Date = today.AddDays(3).AddHours(11),
                EndDate = today.AddDays(3).AddHours(13),
                DurationMinutes = 120,
                Status = AppointmentStatus.Confirmed,
                DepositStatus = DepositStatus.Pending,
                DepositAmount = 0m,
                Notes = "Portrait touch-up session.",
                UpdatedAt = now
            },
            // 4. Confirmed + DepositPaid — Marco + Júlia, 7 days out
            new Appointment
            {
                Id = S1Appt4Id,
                StudioId = Studio1Id,
                ArtistId = S1Artist2Id,
                ClientId = S1Client3Id,
                Date = today.AddDays(7).AddHours(15),
                EndDate = today.AddDays(7).AddHours(18),
                DurationMinutes = 180,
                Status = AppointmentStatus.Confirmed,
                DepositStatus = DepositStatus.Paid,
                DepositAmount = 50m,
                Notes = "Realistic lion on thigh, second session — shading.",
                UpdatedAt = now
            },
            // 5. Confirmed + DepositPaid — Sofia + Rafael, tomorrow
            new Appointment
            {
                Id = S1Appt5Id,
                StudioId = Studio1Id,
                ArtistId = S1Artist3Id,
                ClientId = S1Client4Id,
                Date = today.AddDays(1).AddHours(9),
                EndDate = today.AddDays(1).AddHours(11),
                DurationMinutes = 120,
                Status = AppointmentStatus.Confirmed,
                DepositStatus = DepositStatus.Paid,
                DepositAmount = 50m,
                Notes = "Geometric mandala on upper arm.",
                UpdatedAt = now
            },
            // 6. Completed + DepositPaid — Elena + Ana, 30 days ago
            new Appointment
            {
                Id = S1Appt6Id,
                StudioId = Studio1Id,
                ArtistId = S1Artist1Id,
                ClientId = S1Client1Id,
                Date = today.AddDays(-30).AddHours(10),
                EndDate = today.AddDays(-30).AddHours(14),
                DurationMinutes = 240,
                Status = AppointmentStatus.Completed,
                DepositStatus = DepositStatus.Paid,
                DepositAmount = 60m,
                Notes = "Koi fish full sleeve — session 1 complete.",
                UpdatedAt = now.AddDays(-30)
            },
            // 7. Completed + DepositPaid — Elena + Pedro, 60 days ago
            new Appointment
            {
                Id = S1Appt7Id,
                StudioId = Studio1Id,
                ArtistId = S1Artist1Id,
                ClientId = S1Client2Id,
                Date = today.AddDays(-60).AddHours(13),
                EndDate = today.AddDays(-60).AddHours(16),
                DurationMinutes = 180,
                Status = AppointmentStatus.Completed,
                DepositStatus = DepositStatus.Paid,
                DepositAmount = 50m,
                Notes = "Traditional eagle chest piece — completed.",
                UpdatedAt = now.AddDays(-60)
            },
            // 8. Completed + DepositPaid — Marco + Júlia, 20 days ago (multi-session payment)
            new Appointment
            {
                Id = S1Appt8Id,
                StudioId = Studio1Id,
                ArtistId = S1Artist2Id,
                ClientId = S1Client3Id,
                Date = today.AddDays(-20).AddHours(10),
                EndDate = today.AddDays(-20).AddHours(15),
                DurationMinutes = 300,
                Status = AppointmentStatus.Completed,
                DepositStatus = DepositStatus.Paid,
                DepositAmount = 90m,
                Notes = "Realistic portrait — multi-session project finalised.",
                UpdatedAt = now.AddDays(-20)
            },
            // 9. Completed + DepositPaid — Sofia + Mia, 45 days ago
            new Appointment
            {
                Id = S1Appt9Id,
                StudioId = Studio1Id,
                ArtistId = S1Artist3Id,
                ClientId = S1Client5Id,
                Date = today.AddDays(-45).AddHours(14),
                EndDate = today.AddDays(-45).AddHours(16),
                DurationMinutes = 120,
                Status = AppointmentStatus.Completed,
                DepositStatus = DepositStatus.Paid,
                DepositAmount = 36m,
                Notes = "Fine-line botanical wrist piece.",
                UpdatedAt = now.AddDays(-45)
            },
            // 10. Cancelled + DepositForfeited — Elena + Rafael, 15 days ago
            new Appointment
            {
                Id = S1Appt10Id,
                StudioId = Studio1Id,
                ArtistId = S1Artist1Id,
                ClientId = S1Client4Id,
                Date = today.AddDays(-15).AddHours(11),
                EndDate = today.AddDays(-15).AddHours(13),
                DurationMinutes = 120,
                Status = AppointmentStatus.Cancelled,
                DepositStatus = DepositStatus.Forfeited,
                DepositAmount = 50m,
                Notes = "Client cancelled less than 24h before — deposit forfeited per policy.",
                UpdatedAt = now.AddDays(-15)
            },
            // 11. Cancelled + DepositRefunded — Marco + Mia, 10 days ago
            new Appointment
            {
                Id = S1Appt11Id,
                StudioId = Studio1Id,
                ArtistId = S1Artist2Id,
                ClientId = S1Client5Id,
                Date = today.AddDays(-10).AddHours(15),
                EndDate = today.AddDays(-10).AddHours(18),
                DurationMinutes = 180,
                Status = AppointmentStatus.Cancelled,
                DepositStatus = DepositStatus.Refunded,
                DepositAmount = 50m,
                Notes = "Cancelled 72h in advance — deposit refunded.",
                UpdatedAt = now.AddDays(-10)
            },
            // 12. NoShow + DepositForfeited — Sofia + Ana, 5 days ago
            new Appointment
            {
                Id = S1Appt12Id,
                StudioId = Studio1Id,
                ArtistId = S1Artist3Id,
                ClientId = S1Client1Id,
                Date = today.AddDays(-5).AddHours(9),
                EndDate = today.AddDays(-5).AddHours(11),
                DurationMinutes = 120,
                Status = AppointmentStatus.NoShow,
                DepositStatus = DepositStatus.Forfeited,
                DepositAmount = 50m,
                Notes = "Client did not attend and did not contact the studio.",
                UpdatedAt = now.AddDays(-5)
            },
            // 13. Pending + DepositPending — Sofia + Pedro, 14 days out
            new Appointment
            {
                Id = S1Appt13Id,
                StudioId = Studio1Id,
                ArtistId = S1Artist3Id,
                ClientId = S1Client2Id,
                Date = today.AddDays(14).AddHours(10),
                EndDate = today.AddDays(14).AddHours(13),
                DurationMinutes = 180,
                Status = AppointmentStatus.Pending,
                DepositStatus = DepositStatus.Pending,
                DepositAmount = 0m,
                Notes = "Geometric wolf design — awaiting deposit.",
                UpdatedAt = now
            },
            // 14. Confirmed + DepositPaid — Elena + Mia, 20 days out
            new Appointment
            {
                Id = S1Appt14Id,
                StudioId = Studio1Id,
                ArtistId = S1Artist1Id,
                ClientId = S1Client5Id,
                Date = today.AddDays(20).AddHours(14),
                EndDate = today.AddDays(20).AddHours(16),
                DurationMinutes = 120,
                Status = AppointmentStatus.Confirmed,
                DepositStatus = DepositStatus.Paid,
                DepositAmount = 50m,
                Notes = "Small traditional swallow on ankle.",
                UpdatedAt = now
            },
            // 15. Confirmed + DepositPaid — Marco + Rafael, 25 days out
            new Appointment
            {
                Id = S1Appt15Id,
                StudioId = Studio1Id,
                ArtistId = S1Artist2Id,
                ClientId = S1Client4Id,
                Date = today.AddDays(25).AddHours(10),
                EndDate = today.AddDays(25).AddHours(14),
                DurationMinutes = 240,
                Status = AppointmentStatus.Confirmed,
                DepositStatus = DepositStatus.Paid,
                DepositAmount = 50m,
                Notes = "Memorial portrait — full upper arm.",
                UpdatedAt = now
            }
        );

        await db.SaveChangesAsync();

        // ── Payments ──────────────────────────────────────────────────────────

        // Appt 6 — Paid, €300
        Payment pay6 = new()
        {
            Id = S1Payment6Id,
            StudioId = Studio1Id,
            AppointmentId = S1Appt6Id,
            ClientId = S1Client1Id,
            Amount = 300m,
            Status = PaymentStatus.Paid,
            ProviderReferenceId = "pi_seed_s1_6",
            PaidAt = now.AddDays(-30),
            UpdatedAt = now.AddDays(-30)
        };

        // Appt 7 — Paid, €250
        Payment pay7 = new()
        {
            Id = S1Payment7Id,
            StudioId = Studio1Id,
            AppointmentId = S1Appt7Id,
            ClientId = S1Client2Id,
            Amount = 250m,
            Status = PaymentStatus.Paid,
            ProviderReferenceId = "pi_seed_s1_7",
            PaidAt = now.AddDays(-60),
            UpdatedAt = now.AddDays(-60)
        };

        // Appt 8 — Paid, €450 (multi-session)
        Payment pay8 = new()
        {
            Id = S1Payment8Id,
            StudioId = Studio1Id,
            AppointmentId = S1Appt8Id,
            ClientId = S1Client3Id,
            Amount = 450m,
            Status = PaymentStatus.Paid,
            ProviderReferenceId = "pi_seed_s1_8",
            PaidAt = now.AddDays(-20),
            UpdatedAt = now.AddDays(-20)
        };

        // Appt 9 — Paid, €180
        Payment pay9 = new()
        {
            Id = S1Payment9Id,
            StudioId = Studio1Id,
            AppointmentId = S1Appt9Id,
            ClientId = S1Client5Id,
            Amount = 180m,
            Status = PaymentStatus.Paid,
            ProviderReferenceId = "pi_seed_s1_9",
            PaidAt = now.AddDays(-45),
            UpdatedAt = now.AddDays(-45)
        };

        // Appt 10 — Refunded (deposit forfeited — partial refund of remaining balance)
        Payment pay10 = new()
        {
            Id = S1Payment10Id,
            StudioId = Studio1Id,
            AppointmentId = S1Appt10Id,
            ClientId = S1Client4Id,
            Amount = 50m,
            Status = PaymentStatus.Refunded,
            ProviderReferenceId = "pi_seed_s1_10",
            PaidAt = now.AddDays(-20),
            UpdatedAt = now.AddDays(-15)
        };

        // Appt 11 — Refunded (full deposit refund)
        Payment pay11 = new()
        {
            Id = S1Payment11Id,
            StudioId = Studio1Id,
            AppointmentId = S1Appt11Id,
            ClientId = S1Client5Id,
            Amount = 50m,
            Status = PaymentStatus.Refunded,
            ProviderReferenceId = "pi_seed_s1_11",
            PaidAt = now.AddDays(-15),
            UpdatedAt = now.AddDays(-10)
        };

        db.Payments.AddRange(pay6, pay7, pay8, pay9, pay10, pay11);
        await db.SaveChangesAsync();

        // ── Session Splits (for Appt 8 multi-session payment) ─────────────────

        db.SessionSplits.AddRange(
            new SessionSplit
            {
                StudioId = Studio1Id,
                PaymentId = S1Payment8Id,
                Label = "Session 1 — Outline",
                Amount = 150m,
                PaidAt = now.AddDays(-50),
                UpdatedAt = now.AddDays(-50)
            },
            new SessionSplit
            {
                StudioId = Studio1Id,
                PaymentId = S1Payment8Id,
                Label = "Session 2 — Colour Fill",
                Amount = 150m,
                PaidAt = now.AddDays(-35),
                UpdatedAt = now.AddDays(-35)
            },
            new SessionSplit
            {
                StudioId = Studio1Id,
                PaymentId = S1Payment8Id,
                Label = "Session 3 — Shading & Final Details",
                Amount = 150m,
                PaidAt = now.AddDays(-20),
                UpdatedAt = now.AddDays(-20)
            }
        );

        await db.SaveChangesAsync();

        // ── Designs ───────────────────────────────────────────────────────────

        // Design 1: Koi Fish Full Sleeve — Ana + Elena — two revisions, both approved
        Guid d1Rev1Id = Guid.NewGuid();
        Guid d1Rev2Id = Guid.NewGuid();

        Design design1 = new()
        {
            Id = S1Design1Id,
            StudioId = Studio1Id,
            ClientId = S1Client1Id,
            ArtistId = S1Artist1Id,
            Title = "Koi Fish Full Sleeve",
            Description = "Traditional Japanese koi fish full sleeve with cherry blossoms and waves.",
            UpdatedAt = now.AddDays(-5)
        };

        DesignRevision d1r1 = new()
        {
            Id = d1Rev1Id,
            StudioId = Studio1Id,
            DesignId = S1Design1Id,
            VersionNumber = 1,
            FileUrl = "https://r2.example.com/designs/design1-v1.jpg",
            Notes = "Initial sketch — rough outlines of koi and wave elements.",
            UploadedAt = now.AddDays(-20),
            UpdatedAt = now.AddDays(-20)
        };
        DesignRevision d1r2 = new()
        {
            Id = d1Rev2Id,
            StudioId = Studio1Id,
            DesignId = S1Design1Id,
            VersionNumber = 2,
            FileUrl = "https://r2.example.com/designs/design1-v2.jpg",
            Notes = "Revised per client feedback — added cherry blossom branch upper section.",
            UploadedAt = now.AddDays(-10),
            UpdatedAt = now.AddDays(-10)
        };

        // Design 2: Geometric Wolf — Pedro + Sofia — v1 changes requested, v2 pending
        Guid d2Rev1Id = Guid.NewGuid();
        Guid d2Rev2Id = Guid.NewGuid();

        Design design2 = new()
        {
            Id = S1Design2Id,
            StudioId = Studio1Id,
            ClientId = S1Client2Id,
            ArtistId = S1Artist3Id,
            Title = "Geometric Wolf",
            Description = "Low-poly geometric wolf head with dot-work background.",
            UpdatedAt = now.AddDays(-2)
        };

        DesignRevision d2r1 = new()
        {
            Id = d2Rev1Id,
            StudioId = Studio1Id,
            DesignId = S1Design2Id,
            VersionNumber = 1,
            FileUrl = "https://r2.example.com/designs/design2-v1.jpg",
            Notes = "First draft — wolf head geometric shape.",
            UploadedAt = now.AddDays(-8),
            UpdatedAt = now.AddDays(-8)
        };
        DesignRevision d2r2 = new()
        {
            Id = d2Rev2Id,
            StudioId = Studio1Id,
            DesignId = S1Design2Id,
            VersionNumber = 2,
            FileUrl = "https://r2.example.com/designs/design2-v2.jpg",
            Notes = "Adjusted proportions and added dot-work fill per client request.",
            UploadedAt = now.AddDays(-2),
            UpdatedAt = now.AddDays(-2)
        };

        // Design 3: Portrait Commission — Júlia + Marco — v1 pending review
        Guid d3Rev1Id = Guid.NewGuid();

        Design design3 = new()
        {
            Id = S1Design3Id,
            StudioId = Studio1Id,
            ClientId = S1Client3Id,
            ArtistId = S1Artist2Id,
            Title = "Realistic Portrait Commission",
            Description = "Black & grey realistic portrait of client's late grandmother.",
            UpdatedAt = now.AddDays(-1)
        };

        DesignRevision d3r1 = new()
        {
            Id = d3Rev1Id,
            StudioId = Studio1Id,
            DesignId = S1Design3Id,
            VersionNumber = 1,
            FileUrl = "https://r2.example.com/designs/design3-v1.jpg",
            Notes = "Initial draft from reference photo provided by client.",
            UploadedAt = now.AddDays(-1),
            UpdatedAt = now.AddDays(-1)
        };

        db.Designs.AddRange(design1, design2, design3);
        db.DesignRevisions.AddRange(d1r1, d1r2, d2r1, d2r2, d3r1);
        await db.SaveChangesAsync();

        // ── Design Approvals ──────────────────────────────────────────────────

        db.DesignApprovals.AddRange(
            // D1 Rev1 — Approved
            new DesignApproval
            {
                StudioId = Studio1Id,
                DesignRevisionId = d1Rev1Id,
                Status = DesignApprovalStatus.Approved,
                ClientNotes = "Looks great! I love the initial flow.",
                ReviewedAt = now.AddDays(-18),
                UpdatedAt = now.AddDays(-18)
            },
            // D1 Rev2 — Approved (final)
            new DesignApproval
            {
                StudioId = Studio1Id,
                DesignRevisionId = d1Rev2Id,
                Status = DesignApprovalStatus.Approved,
                ClientNotes = "Perfect! The cherry blossoms are exactly what I wanted. Let's go!",
                ReviewedAt = now.AddDays(-8),
                UpdatedAt = now.AddDays(-8)
            },
            // D2 Rev1 — ChangesRequested
            new DesignApproval
            {
                StudioId = Studio1Id,
                DesignRevisionId = d2Rev1Id,
                Status = DesignApprovalStatus.ChangesRequested,
                ClientNotes = "Love the concept but the wolf's snout looks too elongated. Also could we add more dot-work in the background?",
                ReviewedAt = now.AddDays(-6),
                UpdatedAt = now.AddDays(-6)
            },
            // D2 Rev2 — Pending (awaiting client review)
            new DesignApproval
            {
                StudioId = Studio1Id,
                DesignRevisionId = d2Rev2Id,
                Status = DesignApprovalStatus.Pending,
                ClientNotes = null,
                ReviewedAt = null,
                UpdatedAt = now.AddDays(-2)
            },
            // D3 Rev1 — Pending (just submitted)
            new DesignApproval
            {
                StudioId = Studio1Id,
                DesignRevisionId = d3Rev1Id,
                Status = DesignApprovalStatus.Pending,
                ClientNotes = null,
                ReviewedAt = null,
                UpdatedAt = now.AddDays(-1)
            }
        );

        await db.SaveChangesAsync();

        // ── Intake Forms ──────────────────────────────────────────────────────

        string sampleFormJson = """
            {
              "fullName": "",
              "dateOfBirth": "",
              "hasBloodCondition": false,
              "hasDiabetes": false,
              "takesBloodThinners": false,
              "hasAllergies": false,
              "allergyDetails": "",
              "hasSkinCondition": false,
              "isPregnant": false,
              "acknowledgesAftercare": true
            }
            """;

        db.IntakeForms.AddRange(
            new IntakeForm
            {
                StudioId = Studio1Id,
                ClientId = S1Client1Id,
                AppointmentId = S1Appt6Id,
                FormData = sampleFormJson.Replace("\"fullName\": \"\"", "\"fullName\": \"Ana Costa\"")
                                              .Replace("\"dateOfBirth\": \"\"", "\"dateOfBirth\": \"1992-03-15\""),
                SubmittedAt = now.AddDays(-31),
                UpdatedAt = now.AddDays(-31)
            },
            new IntakeForm
            {
                StudioId = Studio1Id,
                ClientId = S1Client2Id,
                AppointmentId = S1Appt7Id,
                FormData = sampleFormJson.Replace("\"fullName\": \"\"", "\"fullName\": \"Pedro Oliveira\"")
                                              .Replace("\"dateOfBirth\": \"\"", "\"dateOfBirth\": \"1988-07-22\"")
                                              .Replace("\"hasSkinCondition\": false", "\"hasSkinCondition\": true"),
                SubmittedAt = now.AddDays(-61),
                UpdatedAt = now.AddDays(-61)
            },
            new IntakeForm
            {
                StudioId = Studio1Id,
                ClientId = S1Client3Id,
                AppointmentId = S1Appt8Id,
                FormData = sampleFormJson.Replace("\"fullName\": \"\"", "\"fullName\": \"Júlia Ferreira\"")
                                              .Replace("\"dateOfBirth\": \"\"", "\"dateOfBirth\": \"1995-11-05\""),
                SubmittedAt = now.AddDays(-21),
                UpdatedAt = now.AddDays(-21)
            },
            // Rafael — no appointment yet, not yet submitted
            new IntakeForm
            {
                StudioId = Studio1Id,
                ClientId = S1Client4Id,
                AppointmentId = null,
                FormData = sampleFormJson.Replace("\"fullName\": \"\"", "\"fullName\": \"Rafael Mendes\"")
                                              .Replace("\"takesBoodThinners\": false", "\"takesBloodThinners\": true")
                                              .Replace("\"hasAllergies\": false", "\"hasAllergies\": true")
                                              .Replace("\"allergyDetails\": \"\"", "\"allergyDetails\": \"Latex\""),
                SubmittedAt = null,
                UpdatedAt = now
            },
            new IntakeForm
            {
                StudioId = Studio1Id,
                ClientId = S1Client5Id,
                AppointmentId = S1Appt9Id,
                FormData = sampleFormJson.Replace("\"fullName\": \"\"", "\"fullName\": \"Mia Carvalho\"")
                                              .Replace("\"dateOfBirth\": \"\"", "\"dateOfBirth\": \"1998-09-12\""),
                SubmittedAt = now.AddDays(-46),
                UpdatedAt = now.AddDays(-46)
            }
        );

        // ── Consent Forms ─────────────────────────────────────────────────────

        string mockSignature = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        db.ConsentForms.AddRange(
            new ConsentForm
            {
                StudioId = Studio1Id,
                ClientId = S1Client1Id,
                AppointmentId = S1Appt6Id,
                SignedAt = now.AddDays(-30),
                SignatureData = mockSignature,
                FileUrl = "https://r2.example.com/consent/s1-appt6-consent.pdf",
                UpdatedAt = now.AddDays(-30)
            },
            new ConsentForm
            {
                StudioId = Studio1Id,
                ClientId = S1Client2Id,
                AppointmentId = S1Appt7Id,
                SignedAt = now.AddDays(-60),
                SignatureData = mockSignature,
                FileUrl = "https://r2.example.com/consent/s1-appt7-consent.pdf",
                UpdatedAt = now.AddDays(-60)
            },
            new ConsentForm
            {
                StudioId = Studio1Id,
                ClientId = S1Client3Id,
                AppointmentId = S1Appt8Id,
                SignedAt = now.AddDays(-20),
                SignatureData = mockSignature,
                FileUrl = "https://r2.example.com/consent/s1-appt8-consent.pdf",
                UpdatedAt = now.AddDays(-20)
            },
            new ConsentForm
            {
                StudioId = Studio1Id,
                ClientId = S1Client5Id,
                AppointmentId = S1Appt9Id,
                SignedAt = now.AddDays(-45),
                SignatureData = mockSignature,
                FileUrl = "https://r2.example.com/consent/s1-appt9-consent.pdf",
                UpdatedAt = now.AddDays(-45)
            },
            // Upcoming — unsigned (consent not yet collected)
            new ConsentForm
            {
                StudioId = Studio1Id,
                ClientId = S1Client4Id,
                AppointmentId = S1Appt5Id,
                SignedAt = null,
                SignatureData = null,
                FileUrl = null,
                UpdatedAt = now
            }
        );

        await db.SaveChangesAsync();

        // ── Tattoo Records ────────────────────────────────────────────────────

        db.TattooRecords.AddRange(
            new TattooRecord
            {
                StudioId = Studio1Id,
                ClientId = S1Client1Id,
                ArtistId = S1Artist1Id,
                AppointmentId = S1Appt6Id,
                Description = "Traditional Japanese koi fish full sleeve — session 1 (outline complete).",
                BodyLocation = "left-upper-arm",
                PhotoUrls = ["https://r2.example.com/photos/s1-tr1-a.jpg", "https://r2.example.com/photos/s1-tr1-b.jpg"],
                CompletedAt = now.AddDays(-30),
                UpdatedAt = now.AddDays(-30)
            },
            new TattooRecord
            {
                StudioId = Studio1Id,
                ClientId = S1Client2Id,
                ArtistId = S1Artist1Id,
                AppointmentId = S1Appt7Id,
                Description = "Traditional eagle chest piece — fully completed.",
                BodyLocation = "chest",
                PhotoUrls = ["https://r2.example.com/photos/s1-tr2-a.jpg"],
                CompletedAt = now.AddDays(-60),
                UpdatedAt = now.AddDays(-60)
            },
            new TattooRecord
            {
                StudioId = Studio1Id,
                ClientId = S1Client3Id,
                ArtistId = S1Artist2Id,
                AppointmentId = S1Appt8Id,
                Description = "Black & grey realistic portrait on right thigh.",
                BodyLocation = "right-thigh",
                PhotoUrls = ["https://r2.example.com/photos/s1-tr3-a.jpg", "https://r2.example.com/photos/s1-tr3-b.jpg", "https://r2.example.com/photos/s1-tr3-c.jpg"],
                CompletedAt = now.AddDays(-20),
                UpdatedAt = now.AddDays(-20)
            },
            new TattooRecord
            {
                StudioId = Studio1Id,
                ClientId = S1Client5Id,
                ArtistId = S1Artist3Id,
                AppointmentId = S1Appt9Id,
                Description = "Fine-line botanical wrist piece.",
                BodyLocation = "wrist",
                PhotoUrls = ["https://r2.example.com/photos/s1-tr4-a.jpg"],
                CompletedAt = now.AddDays(-45),
                UpdatedAt = now.AddDays(-45)
            }
        );

        // ── Notification Logs ─────────────────────────────────────────────────

        db.NotificationLogs.AddRange(
            // Appointment booking confirmation emails
            new NotificationLog
            {
                StudioId = Studio1Id,
                RecipientId = S1Client1Id,
                Channel = NotificationChannel.Email,
                Subject = "Appointment Confirmed — Ink & Soul Studio",
                Body = "Your appointment on has been confirmed. Please arrive 5 minutes early.",
                SentAt = now.AddDays(-35),
                IsSuccess = true,
                UpdatedAt = now.AddDays(-35)
            },
            new NotificationLog
            {
                StudioId = Studio1Id,
                RecipientId = S1Client2Id,
                Channel = NotificationChannel.Email,
                Subject = "Appointment Confirmed — Ink & Soul Studio",
                Body = "Your appointment has been confirmed. Deposit received.",
                SentAt = now.AddDays(-65),
                IsSuccess = true,
                UpdatedAt = now.AddDays(-65)
            },
            // SMS reminders (24h before)
            new NotificationLog
            {
                StudioId = Studio1Id,
                RecipientId = S1Client1Id,
                Channel = NotificationChannel.Sms,
                Subject = null,
                Body = "Reminder: Your tattoo session at Ink & Soul Studio is tomorrow at 10:00. Reply STOP to unsubscribe.",
                SentAt = now.AddDays(-31),
                IsSuccess = true,
                UpdatedAt = now.AddDays(-31)
            },
            new NotificationLog
            {
                StudioId = Studio1Id,
                RecipientId = S1Client3Id,
                Channel = NotificationChannel.Sms,
                Subject = null,
                Body = "Reminder: Your session at Ink & Soul Studio is tomorrow at 10:00.",
                SentAt = now.AddDays(-21),
                IsSuccess = true,
                UpdatedAt = now.AddDays(-21)
            },
            // Cancellation notices
            new NotificationLog
            {
                StudioId = Studio1Id,
                RecipientId = S1Client4Id,
                Channel = NotificationChannel.Email,
                Subject = "Appointment Cancelled — Deposit Forfeited",
                Body = "Your appointment has been cancelled. As per our policy, the deposit has been forfeited due to the short notice.",
                SentAt = now.AddDays(-15),
                IsSuccess = true,
                UpdatedAt = now.AddDays(-15)
            },
            new NotificationLog
            {
                StudioId = Studio1Id,
                RecipientId = S1Client5Id,
                Channel = NotificationChannel.Email,
                Subject = "Appointment Cancelled — Deposit Refunded",
                Body = "Your appointment has been cancelled and your deposit has been refunded. We hope to see you soon!",
                SentAt = now.AddDays(-10),
                IsSuccess = true,
                UpdatedAt = now.AddDays(-10)
            },
            // Deposit payment confirmation
            new NotificationLog
            {
                StudioId = Studio1Id,
                RecipientId = S1Client2Id,
                Channel = NotificationChannel.Email,
                Subject = "Deposit Received — Ink & Soul Studio",
                Body = "Your deposit of €50 has been received. Your appointment is now confirmed.",
                SentAt = now.AddDays(-11),
                IsSuccess = true,
                UpdatedAt = now.AddDays(-11)
            },
            // Failed SMS attempt
            new NotificationLog
            {
                StudioId = Studio1Id,
                RecipientId = S1Client4Id,
                Channel = NotificationChannel.Sms,
                Subject = null,
                Body = "Reminder: Your session at Ink & Soul Studio is tomorrow at 11:00.",
                SentAt = null,
                IsSuccess = false,
                UpdatedAt = now.AddDays(-16)
            },
            // Design approval notifications
            new NotificationLog
            {
                StudioId = Studio1Id,
                RecipientId = S1Client2Id,
                Channel = NotificationChannel.Email,
                Subject = "New Design Revision Ready for Review",
                Body = "A new revision of your design 'Geometric Wolf' is ready for your review. Please log in to approve or request changes.",
                SentAt = now.AddDays(-2),
                IsSuccess = true,
                UpdatedAt = now.AddDays(-2)
            },
            new NotificationLog
            {
                StudioId = Studio1Id,
                RecipientId = S1Client3Id,
                Channel = NotificationChannel.Email,
                Subject = "Your Design Draft is Ready",
                Body = "Artist Marco has uploaded the first draft of your portrait commission. Please log in to review.",
                SentAt = now.AddDays(-1),
                IsSuccess = true,
                UpdatedAt = now.AddDays(-1)
            }
        );

        await db.SaveChangesAsync();
    }

    // ─── Studio 2: Dark Canvas Tattoo ─────────────────────────────────────────

    private static async Task SeedStudio2EntitiesAsync(AppDbContext db)
    {
        DateTime now = DateTime.UtcNow;
        DateTime today = now.Date;

        // ── Artist ────────────────────────────────────────────────────────────

        Artist s2a1 = new()
        {
            Id = S2Artist1Id,
            StudioId = Studio2Id,
            UserId = Guid.Parse(S2Artist1UserId),
            FirstName = "Luís",
            LastName = "Rodrigues",
            Email = "luis.rodrigues@dark-canvas.test",
            Specializations = "Black & Grey,Lettering,Blackwork",
            HourlyRate = 80m,
            UpdatedAt = now
        };
        s2a1.SetSlug("luis-rodrigues");
        db.Artists.Add(s2a1);

        // ── Portfolio images ─────────────────────────────────────────────────
        db.PortfolioImages.AddRange(
            new PortfolioImage { StudioId = Studio2Id, ArtistId = s2a1.Id, Style = TattooStyle.Blackwork, Category = PortfolioImageCategory.FreshTattoo, ImageUrl = "https://picsum.photos/seed/pena-luis-1/800/1000" },
            new PortfolioImage { StudioId = Studio2Id, ArtistId = s2a1.Id, Style = TattooStyle.Blackwork, Category = PortfolioImageCategory.HealedTattoo, ImageUrl = "https://picsum.photos/seed/pena-luis-2/800/1000" });

        // ── Clients ───────────────────────────────────────────────────────────

        db.Clients.AddRange(
            new Client
            {
                Id = S2Client1Id,
                StudioId = Studio2Id,
                UserId = Guid.Parse(S2Client1UserId),
                FirstName = "Sara",
                LastName = "Lima",
                Email = "sara.lima@dark-canvas.test",
                Phone = "+351963456001",
                UpdatedAt = now
            },
            new Client
            {
                Id = S2Client2Id,
                StudioId = Studio2Id,
                UserId = Guid.Parse(S2Client2UserId),
                FirstName = "Tomás",
                LastName = "Gomes",
                Email = "tomas.gomes@dark-canvas.test",
                Phone = "+351963456002",
                UpdatedAt = now
            }
        );

        // ── Client Profiles ───────────────────────────────────────────────────

        db.ClientProfiles.AddRange(
            new ClientProfile
            {
                StudioId = Studio2Id,
                ClientId = S2Client1Id,
                DateOfBirth = new DateOnly(1994, 6, 8),
                MedicalNotes = "None",
                Allergies = "None",
                BodyMap = new BodyMap { Locations = ["left-forearm", "back-shoulder"] },
                UpdatedAt = now
            },
            new ClientProfile
            {
                StudioId = Studio2Id,
                ClientId = S2Client2Id,
                DateOfBirth = new DateOnly(1991, 2, 14),
                MedicalNotes = "None",
                Allergies = "None",
                BodyMap = new BodyMap { Locations = ["upper-back"] },
                UpdatedAt = now
            }
        );

        // ── Deposit Rule ──────────────────────────────────────────────────────

        db.DepositRules.Add(new DepositRule
        {
            Id = S2DepositRule1Id,
            StudioId = Studio2Id,
            Name = "Standard Deposit (30%)",
            AmountFixed = null,
            AmountPercent = 30m,
            IsActive = true,
            UpdatedAt = now
        });

        await db.SaveChangesAsync();

        // ── Appointments ──────────────────────────────────────────────────────

        db.Appointments.AddRange(
            // 1. Confirmed + DepositPaid — Luís + Sara, 3 days out
            new Appointment
            {
                Id = S2Appt1Id,
                StudioId = Studio2Id,
                ArtistId = S2Artist1Id,
                ClientId = S2Client1Id,
                Date = today.AddDays(3).AddHours(14),
                EndDate = today.AddDays(3).AddHours(17),
                DurationMinutes = 180,
                Status = AppointmentStatus.Confirmed,
                DepositStatus = DepositStatus.Paid,
                DepositAmount = 45m,
                Notes = "Blackwork lettering on forearm — 'Memento Mori'.",
                UpdatedAt = now
            },
            // 2. Pending + DepositPending — Luís + Tomás, 7 days out
            new Appointment
            {
                Id = S2Appt2Id,
                StudioId = Studio2Id,
                ArtistId = S2Artist1Id,
                ClientId = S2Client2Id,
                Date = today.AddDays(7).AddHours(10),
                EndDate = today.AddDays(7).AddHours(14),
                DurationMinutes = 240,
                Status = AppointmentStatus.Pending,
                DepositStatus = DepositStatus.Pending,
                DepositAmount = 0m,
                Notes = "Large back piece — dark forest scene.",
                UpdatedAt = now
            },
            // 3. Completed + DepositPaid — Luís + Sara, 14 days ago
            new Appointment
            {
                Id = S2Appt3Id,
                StudioId = Studio2Id,
                ArtistId = S2Artist1Id,
                ClientId = S2Client1Id,
                Date = today.AddDays(-14).AddHours(11),
                EndDate = today.AddDays(-14).AddHours(13),
                DurationMinutes = 120,
                Status = AppointmentStatus.Completed,
                DepositStatus = DepositStatus.Paid,
                DepositAmount = 30m,
                Notes = "Small blackwork anchor on wrist — completed.",
                UpdatedAt = now.AddDays(-14)
            },
            // 4. Cancelled + DepositRefunded — Luís + Tomás, 7 days ago
            new Appointment
            {
                Id = S2Appt4Id,
                StudioId = Studio2Id,
                ArtistId = S2Artist1Id,
                ClientId = S2Client2Id,
                Date = today.AddDays(-7).AddHours(15),
                EndDate = today.AddDays(-7).AddHours(17),
                DurationMinutes = 120,
                Status = AppointmentStatus.Cancelled,
                DepositStatus = DepositStatus.Refunded,
                DepositAmount = 30m,
                Notes = "Client cancelled — work trip. Full refund issued.",
                UpdatedAt = now.AddDays(-7)
            },
            // 5. Confirmed + DepositPaid — Luís + Sara, 20 days out
            new Appointment
            {
                Id = S2Appt5Id,
                StudioId = Studio2Id,
                ArtistId = S2Artist1Id,
                ClientId = S2Client1Id,
                Date = today.AddDays(20).AddHours(10),
                EndDate = today.AddDays(20).AddHours(14),
                DurationMinutes = 240,
                Status = AppointmentStatus.Confirmed,
                DepositStatus = DepositStatus.Paid,
                DepositAmount = 60m,
                Notes = "Dark forest back piece, session 1.",
                UpdatedAt = now
            }
        );

        await db.SaveChangesAsync();

        // ── Payments ──────────────────────────────────────────────────────────

        db.Payments.AddRange(
            new Payment
            {
                Id = S2Payment3Id,
                StudioId = Studio2Id,
                AppointmentId = S2Appt3Id,
                ClientId = S2Client1Id,
                Amount = 100m,
                Status = PaymentStatus.Paid,
                ProviderReferenceId = "pi_seed_s2_3",
                PaidAt = now.AddDays(-14),
                UpdatedAt = now.AddDays(-14)
            },
            new Payment
            {
                Id = S2Payment4Id,
                StudioId = Studio2Id,
                AppointmentId = S2Appt4Id,
                ClientId = S2Client2Id,
                Amount = 30m,
                Status = PaymentStatus.Refunded,
                ProviderReferenceId = "pi_seed_s2_4",
                PaidAt = now.AddDays(-10),
                UpdatedAt = now.AddDays(-7)
            }
        );

        await db.SaveChangesAsync();

        // ── Design ────────────────────────────────────────────────────────────

        Guid s2d1Rev1Id = Guid.NewGuid();
        Guid s2d1Rev2Id = Guid.NewGuid();

        db.Designs.Add(new Design
        {
            Id = S2Design1Id,
            StudioId = Studio2Id,
            ClientId = S2Client1Id,
            ArtistId = S2Artist1Id,
            Title = "Minimalist Lettering Piece",
            Description = "Single-line cursive lettering 'Memento Mori' with subtle shadow.",
            UpdatedAt = now.AddDays(-1)
        });

        db.DesignRevisions.AddRange(
            new DesignRevision
            {
                Id = s2d1Rev1Id,
                StudioId = Studio2Id,
                DesignId = S2Design1Id,
                VersionNumber = 1,
                FileUrl = "https://r2.example.com/designs/s2-design1-v1.jpg",
                Notes = "First draft — standard font.",
                UploadedAt = now.AddDays(-5),
                UpdatedAt = now.AddDays(-5)
            },
            new DesignRevision
            {
                Id = s2d1Rev2Id,
                StudioId = Studio2Id,
                DesignId = S2Design1Id,
                VersionNumber = 2,
                FileUrl = "https://r2.example.com/designs/s2-design1-v2.jpg",
                Notes = "Custom calligraphic font applied — more personal feel.",
                UploadedAt = now.AddDays(-1),
                UpdatedAt = now.AddDays(-1)
            }
        );

        await db.SaveChangesAsync();

        db.DesignApprovals.AddRange(
            new DesignApproval
            {
                StudioId = Studio2Id,
                DesignRevisionId = s2d1Rev1Id,
                Status = DesignApprovalStatus.ChangesRequested,
                ClientNotes = "Can we try a more custom font? Something less standard.",
                ReviewedAt = now.AddDays(-4),
                UpdatedAt = now.AddDays(-4)
            },
            new DesignApproval
            {
                StudioId = Studio2Id,
                DesignRevisionId = s2d1Rev2Id,
                Status = DesignApprovalStatus.Pending,
                ClientNotes = null,
                ReviewedAt = null,
                UpdatedAt = now.AddDays(-1)
            }
        );

        // Intake + Consent
        string formData = "{\"fullName\":\"\",\"acknowledgesAftercare\":true}";

        db.IntakeForms.AddRange(
            new IntakeForm
            {
                StudioId = Studio2Id,
                ClientId = S2Client1Id,
                AppointmentId = S2Appt3Id,
                FormData = formData.Replace("\"fullName\":\"\"", "\"fullName\":\"Sara Lima\""),
                SubmittedAt = now.AddDays(-14),
                UpdatedAt = now.AddDays(-14)
            },
            new IntakeForm
            {
                StudioId = Studio2Id,
                ClientId = S2Client2Id,
                AppointmentId = null,
                FormData = formData.Replace("\"fullName\":\"\"", "\"fullName\":\"Tomás Gomes\""),
                SubmittedAt = null,
                UpdatedAt = now
            }
        );

        string mockSig = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        db.ConsentForms.Add(new ConsentForm
        {
            StudioId = Studio2Id,
            ClientId = S2Client1Id,
            AppointmentId = S2Appt3Id,
            SignedAt = now.AddDays(-14),
            SignatureData = mockSig,
            FileUrl = "https://r2.example.com/consent/s2-appt3-consent.pdf",
            UpdatedAt = now.AddDays(-14)
        });

        db.TattooRecords.Add(new TattooRecord
        {
            StudioId = Studio2Id,
            ClientId = S2Client1Id,
            ArtistId = S2Artist1Id,
            AppointmentId = S2Appt3Id,
            Description = "Blackwork anchor on left wrist.",
            BodyLocation = "wrist",
            PhotoUrls = ["https://r2.example.com/photos/s2-tr1-a.jpg"],
            CompletedAt = now.AddDays(-14),
            UpdatedAt = now.AddDays(-14)
        });

        db.NotificationLogs.AddRange(
            new NotificationLog
            {
                StudioId = Studio2Id,
                RecipientId = S2Client1Id,
                Channel = NotificationChannel.Email,
                Subject = "Appointment Confirmed — Dark Canvas Tattoo",
                Body = "Your appointment is confirmed. Deposit received.",
                SentAt = now.AddDays(-15),
                IsSuccess = true,
                UpdatedAt = now.AddDays(-15)
            },
            new NotificationLog
            {
                StudioId = Studio2Id,
                RecipientId = S2Client2Id,
                Channel = NotificationChannel.Sms,
                Subject = null,
                Body = "Your deposit refund has been processed. We hope to see you again soon!",
                SentAt = now.AddDays(-7),
                IsSuccess = true,
                UpdatedAt = now.AddDays(-7)
            }
        );

        await db.SaveChangesAsync();
    }

    // ─── Helper ───────────────────────────────────────────────────────────────

    private static async Task EnsureUserAsync(
        UserManager<IdentityUser> userManager,
        string userId,
        string email,
        string role,
        Guid studioId,
        string firstName)
    {
        IdentityUser? existing = await userManager.FindByIdAsync(userId);

        if (existing is null)
        {
            IdentityUser user = new() { Id = userId, UserName = email, Email = email };
            IdentityResult result = await userManager.CreateAsync(user, Password);

            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Seed user '{email}' failed: {string.Join(", ", result.Errors.Select(e => e.Description))}");

            await userManager.AddToRoleAsync(user, role);
            await userManager.AddClaimAsync(user, new Claim("tenant_id", studioId.ToString()));
            await userManager.AddClaimAsync(user, new Claim(JwtRegisteredClaimNames.GivenName, firstName));
        }
        else
        {
            if (!await userManager.CheckPasswordAsync(existing, Password))
            {
                await userManager.RemovePasswordAsync(existing);
                await userManager.AddPasswordAsync(existing, Password);
            }

            // Sync given_name: add if absent, replace if the seed name changed.
            IList<Claim> claims = await userManager.GetClaimsAsync(existing);
            Claim? existingNameClaim = claims.FirstOrDefault(c => c.Type == JwtRegisteredClaimNames.GivenName);
            if (existingNameClaim is null)
                await userManager.AddClaimAsync(existing, new Claim(JwtRegisteredClaimNames.GivenName, firstName));
            else if (existingNameClaim.Value != firstName)
                await userManager.ReplaceClaimAsync(existing, existingNameClaim, new Claim(JwtRegisteredClaimNames.GivenName, firstName));
        }
    }
}
