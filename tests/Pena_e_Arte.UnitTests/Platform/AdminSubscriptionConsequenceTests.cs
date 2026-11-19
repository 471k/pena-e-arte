using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Platform;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

/// <summary>Slice 8 phase 3: the admin cancellation's consequence (D03) and failed refunds as a work list (D07).</summary>
public class AdminSubscriptionConsequenceTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    // ── D03: the cancellation quote ──────────────────────────────────────────

    [Fact]
    public async Task Quote_MonthlySubscription_RefundsNothing_AndSaysSo()
    {
        Guid studioId = await SeedSubscription(SubscriptionStatus.Active, BillingInterval.Monthly, stripeId: "sub_m", billedCurrency: "eur");

        AdminCancellationQuoteResponse quote = await Quote(studioId);

        quote.Status.Should().Be("Active");
        quote.BillingInterval.Should().Be("Monthly");
        quote.BilledThroughStripe.Should().BeTrue();
        quote.RefundApplies.Should().BeFalse();
        quote.FormulaRefund.Should().Be(0m);
        quote.FullRefund.Should().Be(0m);
        quote.InvoiceId.Should().BeNull();
        quote.Currency.Should().Be("eur");
    }

    [Fact]
    public async Task Quote_YearlyWithPaidInvoice_GivesTheFormulaAndTheFullFigure_InTheInvoiceCurrency()
    {
        Guid studioId = await SeedSubscription(SubscriptionStatus.Active, BillingInterval.Yearly, stripeId: "sub_y", billedCurrency: "usd");
        Guid invoiceId = await SeedInvoice(studioId, amountPaid: 790m, monthlyRef: 79m, monthsAgo: 3, currency: "eur");

        AdminCancellationQuoteResponse quote = await Quote(studioId);

        quote.RefundApplies.Should().BeTrue();
        quote.FormulaRefund.Should().Be(553m); // 790 - 3 x 79, the same figure CancelSubscriptionHandlerTests asserts
        quote.MonthsUsed.Should().Be(3);
        quote.FullRefund.Should().Be(790m);
        quote.AmountPaid.Should().Be(790m);
        quote.InvoiceId.Should().Be(invoiceId);
        quote.Currency.Should().Be("eur"); // the invoice's, not the subscription's
    }

    [Fact]
    public async Task Quote_YearlyButNotStripeBilled_RefundsNothing()
    {
        Guid studioId = await SeedSubscription(SubscriptionStatus.Active, BillingInterval.Yearly, stripeId: null, billedCurrency: "eur");

        AdminCancellationQuoteResponse quote = await Quote(studioId);

        quote.RefundApplies.Should().BeFalse();
        quote.BilledThroughStripe.Should().BeFalse();
    }

    [Fact]
    public async Task Quote_NotCancellable_RefusesWithTheCommandsOwnMessage()
    {
        Guid studioId = await SeedSubscription(SubscriptionStatus.Cancelled, BillingInterval.Monthly, stripeId: null, billedCurrency: null);

        Func<Task> act = () => Quote(studioId);

        (await act.Should().ThrowAsync<BusinessRuleViolationException>())
            .WithMessage("A subscription with status 'Cancelled' cannot be cancelled.");
    }

    [Fact]
    public async Task Quote_NoSubscription_Refuses()
    {
        Func<Task> act = () => Quote(Guid.NewGuid());

        (await act.Should().ThrowAsync<BusinessRuleViolationException>())
            .WithMessage("Studio has no subscription to cancel.");
    }

    [Fact]
    public void QuoteValidator_EmptyStudioId_HasError()
    {
        new GetAdminCancellationQuoteValidator().Validate(new GetAdminCancellationQuoteQuery(Guid.Empty))
            .IsValid.Should().BeFalse();
        new GetAdminCancellationQuoteValidator().Validate(new GetAdminCancellationQuoteQuery(Guid.NewGuid()))
            .IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, true, null)]
    [InlineData("AdminFull", true, RefundRule.AdminFull)]
    [InlineData("ADMINNONE", true, RefundRule.AdminNone)]
    [InlineData("YearlyFormula", false, null)]
    [InlineData("1", false, null)]
    [InlineData("2", false, null)]
    [InlineData(" AdminFull", false, null)]
    public void TryParseOverride_AcceptsTheTwoAdminRuleNamesOnly(string? text, bool ok, RefundRule? expected)
    {
        AdminCancellationQuotes.TryParseOverride(text, out RefundRule? rule).Should().Be(ok);
        rule.Should().Be(expected);
    }

    // ── D07: failed refunds across studios ───────────────────────────────────

    [Fact]
    public async Task FailedRefunds_ListsOnlyFailed_NewestFailureFirst_WithStudioAndInvoice()
    {
        Guid a = await SeedStudio("Studio A");
        Guid b = await SeedStudio("Studio B");
        SeedRefund(a, RefundStatus.Failed, 100m, "eur", updatedAt: new DateTime(2026, 11, 10, 9, 0, 0, DateTimeKind.Utc), reason: "card_expired", invoice: "in_older");
        SeedRefund(b, RefundStatus.Failed, 250m, "usd", updatedAt: new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc), reason: null, invoice: "in_newer",
            stripeRefundId: "re_newer");
        SeedRefund(a, RefundStatus.Succeeded, 50m, "eur", updatedAt: null, reason: null, invoice: "in_ok");
        SeedRefund(b, RefundStatus.Pending, 70m, "eur", updatedAt: null, reason: null, invoice: "in_wait");
        await _db.SaveChangesAsync();

        FailedSubscriptionRefundsResponse result = await new GetFailedSubscriptionRefundsHandler(_db)
            .Handle(new GetFailedSubscriptionRefundsQuery(), default);

        result.TotalCount.Should().Be(2);
        result.Items.Select(i => i.InvoiceReference).Should().Equal("in_newer", "in_older");
        FailedSubscriptionRefundResponse newest = result.Items[0];
        newest.StudioId.Should().Be(b);
        newest.StudioName.Should().Be("Studio B");
        newest.Amount.Should().Be(250m);
        newest.Currency.Should().Be("usd");
        newest.FailedAt.Should().Be(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc));
        result.Items[1].FailureReason.Should().Be("card_expired");
        newest.ReachedStripe.Should().BeTrue();          // Stripe accepted it, then reported it failed
        result.Items[1].ReachedStripe.Should().BeFalse(); // the request itself failed
    }

    [Fact]
    public async Task FailedRefunds_None_IsAnEmptyListAndAZeroCount()
    {
        FailedSubscriptionRefundsResponse result = await new GetFailedSubscriptionRefundsHandler(_db)
            .Handle(new GetFailedSubscriptionRefundsQuery(), default);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task FailedRefunds_MoreThanTheLimit_ReturnsTheLimit_AndTheTrueTotal()
    {
        Guid studioId = await SeedStudio("Busy Studio");
        int count = GetFailedSubscriptionRefundsQuery.Limit + 3;
        for (int i = 0; i < count; i++)
            SeedRefund(studioId, RefundStatus.Failed, 10m, "eur",
                updatedAt: new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc).AddHours(i), reason: "x", invoice: $"in_{i}");
        await _db.SaveChangesAsync();

        FailedSubscriptionRefundsResponse result = await new GetFailedSubscriptionRefundsHandler(_db)
            .Handle(new GetFailedSubscriptionRefundsQuery(), default);

        result.Items.Should().HaveCount(GetFailedSubscriptionRefundsQuery.Limit);
        result.TotalCount.Should().Be(count);
        result.Items[0].InvoiceReference.Should().Be($"in_{count - 1}");
    }

    [Fact]
    public async Task StudioSummary_RecentRefunds_CarryTheirOwnCurrencyAndInvoice()
    {
        // Evidence §0.3: the page rendered a hardcoded euro because the response had no currency to render.
        Guid studioId = await SeedStudio("Currency Studio");
        SeedRefund(studioId, RefundStatus.Failed, 99m, "usd", updatedAt: new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc),
            reason: "insufficient_funds", invoice: "in_usd");
        await _db.SaveChangesAsync();

        AdminStudioSummaryResponse summary = await new GetAdminStudioSummaryHandler(_db, Substitute.For<IIdentityService>())
            .Handle(new GetAdminStudioSummaryQuery(studioId), default);

        RecentRefundResponse refund = summary.RecentRefunds.Should().ContainSingle().Subject;
        refund.Currency.Should().Be("usd");
        refund.InvoiceReference.Should().Be("in_usd");
        refund.FailureReason.Should().Be("insufficient_funds");
        refund.UpdatedAt.Should().Be(new DateTime(2026, 11, 12, 8, 0, 0, DateTimeKind.Utc));
        refund.ReachedStripe.Should().BeFalse();
    }

    // ── seeding ──────────────────────────────────────────────────────────────

    private Task<AdminCancellationQuoteResponse> Quote(Guid studioId) =>
        new GetAdminCancellationQuoteHandler(_db, TimeProvider.System).Handle(new GetAdminCancellationQuoteQuery(studioId), default);

    private async Task<Guid> SeedStudio(string name)
    {
        Studio studio = new() { Name = name, Slug = Guid.NewGuid().ToString("N")[..20] };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        return studio.Id;
    }

    private async Task<Guid> SeedSubscription(
        SubscriptionStatus status, BillingInterval interval, string? stripeId, string? billedCurrency)
    {
        Guid studioId = await SeedStudio($"Sub {Guid.NewGuid():N}"[..12]);
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = studioId,
            Status = status,
            BillingInterval = interval,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(30),
            StripeSubscriptionId = stripeId,
            BilledCurrency = billedCurrency,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return studioId;
    }

    private async Task<Guid> SeedInvoice(Guid studioId, decimal amountPaid, decimal monthlyRef, int monthsAgo, string currency)
    {
        Subscription sub = _db.Subscriptions.Single(s => s.StudioId == studioId);
        // Same month-end-proof seed as CancelSubscriptionHandlerTests.SeedYearlyStudioWithInvoice.
        SubscriptionInvoicePayment invoice = new()
        {
            SubscriptionId = sub.Id,
            StudioId = studioId,
            StripeInvoiceId = $"in_{Guid.NewGuid():N}",
            AmountPaid = amountPaid,
            Currency = currency,
            PaidAt = DateTime.UtcNow.AddMonths(-(monthsAgo - 1)).AddDays(-15),
            PeriodStart = DateTime.UtcNow.AddMonths(-(monthsAgo - 1)).AddDays(-15),
            MonthlyReferencePrice = monthlyRef,
            StripePaymentIntentId = $"pi_{Guid.NewGuid():N}",
        };
        _db.SubscriptionInvoicePayments.Add(invoice);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return invoice.Id;
    }

    private void SeedRefund(
        Guid studioId, RefundStatus status, decimal amount, string currency, DateTime? updatedAt, string? reason, string invoice,
        string? stripeRefundId = null) =>
        _db.SubscriptionRefunds.Add(new SubscriptionRefund
        {
            SubscriptionId = Guid.NewGuid(),
            StudioId = studioId,
            SubscriptionInvoicePaymentId = Guid.NewGuid(),
            StripeInvoiceId = invoice,
            StripeRefundId = stripeRefundId,
            Amount = amount,
            Currency = currency,
            Status = status,
            FailureReason = reason,
            UpdatedAt = updatedAt,
            Rule = RefundRule.YearlyFormula,
        });
}
