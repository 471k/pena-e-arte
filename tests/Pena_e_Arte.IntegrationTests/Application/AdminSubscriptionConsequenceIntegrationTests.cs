using FluentAssertions;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// Slice 8 phase 3 on real MySQL: the admin cancellation quote (D03) and the platform-wide failed-refund list (D07).
/// Both read tables with no tenant filter (Subscription, SubscriptionInvoicePayment, SubscriptionRefund, Studio), from a
/// context with no tenant at all, which is how the AdminOnly endpoints run. The shared database holds other tests' rows,
/// so assertions pick this test's own rows out by their invoice references rather than counting.
/// </summary>
[Collection("Database")]
public class AdminSubscriptionConsequenceIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task CancellationQuote_YearlyWithPaidInvoice_TranslatesAndComputesTheFormula()
    {
        string tag = Tag();
        (Guid studioId, Guid subscriptionId) = await SeedSubscription(tag, BillingInterval.Yearly, $"sub_{tag}");
        Guid invoiceId = await SeedInvoice(studioId, subscriptionId, $"in_{tag}", amountPaid: 790m, monthlyRef: 79m, monthsAgo: 3);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        AdminCancellationQuoteResponse quote = await new GetAdminCancellationQuoteHandler(db, TimeProvider.System)
            .Handle(new GetAdminCancellationQuoteQuery(studioId), default);

        quote.RefundApplies.Should().BeTrue();
        quote.InvoiceId.Should().Be(invoiceId);
        quote.FormulaRefund.Should().Be(553m);
        quote.FullRefund.Should().Be(790m);
        quote.Currency.Should().Be("eur");
        quote.PlanName.Should().BeNull(); // no plan seeded: the Include must not turn a missing plan into a missing row
    }

    [Fact]
    public async Task FailedRefunds_ListsFailedRowsWithTheirStudio_NewestFirst_AndNotOtherStatuses()
    {
        string tag = Tag();
        (Guid studioId, Guid subscriptionId) = await SeedSubscription(tag, BillingInterval.Yearly, $"sub_{tag}");
        DateTime older = DateTime.UtcNow.AddMinutes(-20);
        DateTime newer = DateTime.UtcNow.AddMinutes(-10);
        await SeedRefund(studioId, subscriptionId, $"in_{tag}_a", RefundStatus.Failed, older, "card_expired");
        await SeedRefund(studioId, subscriptionId, $"in_{tag}_b", RefundStatus.Failed, newer, null);
        await SeedRefund(studioId, subscriptionId, $"in_{tag}_c", RefundStatus.Succeeded, newer, null);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        FailedSubscriptionRefundsResponse result = await new GetFailedSubscriptionRefundsHandler(db)
            .Handle(new GetFailedSubscriptionRefundsQuery(), default);

        List<FailedSubscriptionRefundResponse> mine = result.Items.Where(i => i.StudioId == studioId).ToList();
        mine.Select(i => i.InvoiceReference).Should().Equal($"in_{tag}_b", $"in_{tag}_a");
        mine.Should().AllSatisfy(i =>
        {
            i.StudioName.Should().Be($"Refund Studio {tag}");
            i.Currency.Should().Be("eur");
            i.Rule.Should().Be("YearlyFormula");
        });
        mine[1].FailureReason.Should().Be("card_expired");
        result.TotalCount.Should().BeGreaterThanOrEqualTo(2);
    }

    private static string Tag() => Guid.NewGuid().ToString("N")[..10];

    private async Task<(Guid StudioId, Guid SubscriptionId)> SeedSubscription(string tag, BillingInterval interval, string stripeId)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = $"Refund Studio {tag}",
            Slug = $"refund-{tag}",
            City = "Tirana",
            OwnerEmail = $"{tag}@test.com",
            IsActive = true,
            TrialExpiresAt = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
        };
        db.Studios.Add(studio);
        Subscription subscription = new()
        {
            StudioId = studio.Id,
            Status = SubscriptionStatus.Active,
            BillingInterval = interval,
            CurrentPeriodEnd = DateTime.UtcNow.AddMonths(9),
            StripeSubscriptionId = stripeId,
        };
        db.Subscriptions.Add(subscription);
        await db.SaveChangesAsync();
        return (studio.Id, subscription.Id);
    }

    private async Task<Guid> SeedInvoice(
        Guid studioId, Guid subscriptionId, string invoiceRef, decimal amountPaid, decimal monthlyRef, int monthsAgo)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        // Month-end-proof seed, as in CancelSubscriptionHandlerTests: both neighbouring anniversaries stay 12+ days away.
        SubscriptionInvoicePayment invoice = new()
        {
            SubscriptionId = subscriptionId,
            StudioId = studioId,
            StripeInvoiceId = invoiceRef,
            AmountPaid = amountPaid,
            Currency = "eur",
            PaidAt = DateTime.UtcNow.AddMonths(-(monthsAgo - 1)).AddDays(-15),
            PeriodStart = DateTime.UtcNow.AddMonths(-(monthsAgo - 1)).AddDays(-15),
            MonthlyReferencePrice = monthlyRef,
            StripePaymentIntentId = $"pi_{invoiceRef}",
        };
        db.SubscriptionInvoicePayments.Add(invoice);
        await db.SaveChangesAsync();
        return invoice.Id;
    }

    private async Task SeedRefund(
        Guid studioId, Guid subscriptionId, string invoiceRef, RefundStatus status, DateTime updatedAt, string? reason)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        db.SubscriptionRefunds.Add(new SubscriptionRefund
        {
            SubscriptionId = subscriptionId,
            StudioId = studioId,
            SubscriptionInvoicePaymentId = Guid.NewGuid(), // unique per row; there is no FK to the invoice table
            StripeInvoiceId = invoiceRef,
            Amount = 100m,
            Currency = "eur",
            AmountPaid = 790m,
            MonthlyReferencePrice = 79m,
            Rule = RefundRule.YearlyFormula,
            InitiatedByUserId = Guid.NewGuid(),
            Status = status,
            FailureReason = reason,
            UpdatedAt = updatedAt,
        });
        await db.SaveChangesAsync();
    }
}
