using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.IntegrationTests.Infrastructure;

[Collection("Database")]
public class TrialExpiryWarningJobTests(DatabaseFixture fixture)
{
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();

    private TrialExpiryWarningJob CreateSut(AppDbContext db) =>
        new(_notifications, db, _realtime, NullLogger<TrialExpiryWarningJob>.Instance);

    [Fact]
    public async Task ExecuteAsync_ValidStudio_SendsEmailToOwner()
    {
        Guid studioId = await SeedStudio("owner@mystudio.com");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await _notifications.Received(1).SendEmailAsync(
            "owner@mystudio.com",
            Arg.Is<string>(s => s.Contains("48 hours")),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ValidStudio_WritesSuccessNotificationLog()
    {
        Guid studioId = await SeedStudio("owner@success.com");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        NotificationLog? log = await verify.NotificationLogs
            .FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Email);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeTrue();
        log.StudioId.Should().Be(studioId);
        log.RecipientType.Should().Be(NotificationRecipientType.Studio);
    }

    [Fact]
    public async Task ExecuteAsync_EmailFails_WritesFailedNotificationLog()
    {
        Guid studioId = await SeedStudio("owner@fail.com");

        _notifications.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                      .ThrowsAsync(new InvalidOperationException("SMTP down"));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        NotificationLog? log = await verify.NotificationLogs
            .FirstOrDefaultAsync(n => n.Channel == NotificationChannel.Email);
        log.Should().NotBeNull();
        log!.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_EmailFails_DoesNotThrow()
    {
        Guid studioId = await SeedStudio("owner@nothrow.com");

        _notifications.SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
                      .ThrowsAsync(new InvalidOperationException("SMTP down"));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);

        Func<Task> act = () => CreateSut(db).ExecuteAsync(studioId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_UnknownStudioId_DoesNotSendOrThrow()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);

        Func<Task> act = () => CreateSut(db).ExecuteAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
        await _notifications.DidNotReceive().SendEmailAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ValidStudio_PushesNotificationReceivedEvent()
    {
        Guid studioId = await SeedStudio("owner@realtime.com");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await _realtime.Received(1).NotifyStudioAsync(
            studioId, "NotificationReceived", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ValidStudio_PushesRecipientNameResolvedFromStudio()
    {
        Guid studioId = await SeedStudio("owner@names.com", name: "Tinta Viva");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await _realtime.Received(1).NotifyStudioAsync(
            studioId, "NotificationReceived",
            Arg.Is<NotificationLogResponse>(r => r.RecipientName == "Tinta Viva"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_EmailBodyContainsStudioName()
    {
        Guid studioId = await SeedStudio("owner@body.com", name: "Tinta Viva");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await _notifications.Received(1).SendEmailAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("Tinta Viva")),
            Arg.Any<CancellationToken>());
    }

    // ── Yearly saving paragraph (D7, §7.4) ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EveryPaidTierHasSameWholeMonthsFreeYearly_ShowsCatalogWideLine()
    {
        await ResetPlansAsync();
        await SeedPurchasableTier("Starter", 29m, 290m);
        await SeedPurchasableTier("Growth", 59m, 590m);
        await SeedPurchasableTier("Premium", 79m, 790m);
        Guid studioId = await SeedStudio("owner@all-yearly.com");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await _notifications.Received(1).SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("Pay yearly and get 2 months free.")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_OnlySomePaidTiersHavePurchasableYearly_NamesThem()
    {
        await ResetPlansAsync();
        // Starter has an unlinked Yearly row (StripePriceId null — not purchasable, D4);
        // Premium's is linked. Matches the real current-catalogue state (Batch 3 pending).
        await SeedPlan("Starter", 29m, yearlyPrice: 290m, yearlyStripePriceId: null);
        await SeedPurchasableTier("Premium", 79m, 790m);
        Guid studioId = await SeedStudio("owner@some-yearly.com");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await _notifications.Received(1).SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<string>(body => body.Contains("Yearly billing is available on Premium — 2 months free.")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_NoPurchasableYearlyPrice_OmitsParagraph()
    {
        await ResetPlansAsync();
        await SeedPlan("Starter", 29m, yearlyPrice: null, yearlyStripePriceId: null);
        Guid studioId = await SeedStudio("owner@no-yearly.com");

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        await CreateSut(db).ExecuteAsync(studioId);

        await _notifications.Received(1).SendEmailAsync(
            Arg.Any<string>(), Arg.Any<string>(),
            Arg.Is<string>(body => !body.Contains("months free") && !body.Contains("Yearly billing is available")),
            Arg.Any<CancellationToken>());
    }

    // Deactivate rather than delete — the "Database" collection shares one physical DB
    // across every integration test class, so other tests' Plan rows may already be
    // referenced by their own Subscriptions (delete would violate that FK). Flipping
    // IsActive false makes them invisible to BuildYearlySavingParagraphAsync's active-
    // price filters without touching anything another test still depends on.
    private async Task ResetPlansAsync()
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        List<PlanPrice> active = await db.PlanPrices.Where(pp => pp.IsActive).ToListAsync();
        foreach (PlanPrice pp in active)
            pp.IsActive = false;
        await db.SaveChangesAsync();
    }

    private Task SeedPurchasableTier(string name, decimal monthlyPrice, decimal yearlyPrice) =>
        SeedPlan(name, monthlyPrice, yearlyPrice, yearlyStripePriceId: $"price_{name.ToLowerInvariant()}_yearly");

    private async Task SeedPlan(
        string name, decimal monthlyPrice, decimal? yearlyPrice, string? yearlyStripePriceId)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        Plan plan = new() { Name = name };
        plan.Prices.Add(new PlanPrice
        {
            Interval = BillingInterval.Monthly, Price = monthlyPrice, StripePriceId = $"price_{name.ToLowerInvariant()}_monthly",
        });
        if (yearlyPrice is decimal yp)
        {
            plan.Prices.Add(new PlanPrice
            {
                Interval = BillingInterval.Yearly, Price = yp, StripePriceId = yearlyStripePriceId,
            });
        }
        db.Plans.Add(plan);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedStudio(string ownerEmail, string name = "Test Studio")
    {
        await using AppDbContext ctx = fixture.CreateDbContext(Guid.Empty);

        Studio studio = new()
        {
            Name = name,
            Slug = ("s-" + Guid.NewGuid().ToString("N"))[..20],
            City = "Porto",
            OwnerEmail = ownerEmail,
            IsActive = true,
            TrialExpiresAt = DateTime.UtcNow.AddDays(2)
        };
        ctx.Studios.Add(studio);

        ctx.Subscriptions.Add(new Subscription
        {
            StudioId = studio.Id,
            Status = SubscriptionStatus.Trialing,
            TrialExpiresAt = studio.TrialExpiresAt,
            GracePeriodEnd = studio.TrialExpiresAt.AddDays(7),
            CurrentPeriodEnd = studio.TrialExpiresAt
        });

        await ctx.SaveChangesAsync();
        return studio.Id;
    }
}
