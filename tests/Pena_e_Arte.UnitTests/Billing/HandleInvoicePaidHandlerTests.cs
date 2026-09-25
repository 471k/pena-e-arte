using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class HandleInvoicePaidHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private readonly IStripeBillingService _stripe = Substitute.For<IStripeBillingService>();

    private HandleInvoicePaidHandler CreateSut() =>
        new(_db, _stripe, NullLogger<HandleInvoicePaidHandler>.Instance);

    private static HandleInvoicePaidCommand Command(
        string stripeSubId, DateTime periodEnd, string invoiceId = "in_1",
        decimal amountPaid = 79m, decimal discountAmount = 0m, string currency = "eur",
        DateTime? paidAt = null) =>
        new(stripeSubId, periodEnd, invoiceId, amountPaid, discountAmount, currency, paidAt ?? DateTime.UtcNow);

    [Fact]
    public async Task Handle_KnownSubscription_SetsStatusToActive()
    {
        string stripeSubId = "sub_abc123";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Trialing);
        DateTime periodEnd = DateTime.UtcNow.AddMonths(1);

        await CreateSut().Handle(Command(stripeSubId, periodEnd), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .Status.Should().Be(SubscriptionStatus.Active);
    }

    [Fact]
    public async Task Handle_KnownSubscription_UpdatesCurrentPeriodEnd()
    {
        string stripeSubId = "sub_xyz789";
        DateTime periodEnd = DateTime.UtcNow.AddMonths(1);
        await SeedSubscription(stripeSubId, SubscriptionStatus.Trialing);

        await CreateSut().Handle(Command(stripeSubId, periodEnd), default);

        _db.Subscriptions.Single(s => s.StripeSubscriptionId == stripeSubId)
            .CurrentPeriodEnd.Should().Be(periodEnd);
    }

    [Fact]
    public async Task Handle_UnknownSubscription_DoesNotThrow()
    {
        Func<Task> act = () => CreateSut().Handle(
            Command("sub_unknown", DateTime.UtcNow.AddMonths(1)), default);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Handle_KnownSubscription_RecordsInvoicePayment()
    {
        string stripeSubId = "sub_record1";
        Guid subId = await SeedSubscription(stripeSubId, SubscriptionStatus.Trialing);
        DateTime paidAt = new(2026, 9, 24, 10, 0, 0, DateTimeKind.Utc);

        await CreateSut().Handle(
            Command(stripeSubId, DateTime.UtcNow.AddMonths(1), "in_record1", 79m, 10m, "eur", paidAt), default);

        SubscriptionInvoicePayment payment = _db.SubscriptionInvoicePayments.Single(p => p.StripeInvoiceId == "in_record1");
        payment.SubscriptionId.Should().Be(subId);
        payment.AmountPaid.Should().Be(79m);
        payment.DiscountAmount.Should().Be(10m);
        payment.Currency.Should().Be("eur");
        payment.PaidAt.Should().Be(paidAt);
    }

    [Fact]
    public async Task Handle_SameInvoiceIdTwice_DoesNotDoubleCount()
    {
        string stripeSubId = "sub_dup1";
        await SeedSubscription(stripeSubId, SubscriptionStatus.Trialing);

        HandleInvoicePaidCommand command = Command(stripeSubId, DateTime.UtcNow.AddMonths(1), "in_dup1");
        await CreateSut().Handle(command, default);
        await CreateSut().Handle(command, default);

        _db.SubscriptionInvoicePayments.Count(p => p.StripeInvoiceId == "in_dup1").Should().Be(1);
    }

    private async Task<Guid> SeedSubscription(string stripeSubId, SubscriptionStatus status)
    {
        Subscription sub = new()
        {
            StudioId = Guid.NewGuid(),
            StripeSubscriptionId = stripeSubId,
            Status = status,
            TrialExpiresAt = DateTime.UtcNow.AddDays(14),
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(14),
            GracePeriodEnd = DateTime.UtcNow.AddDays(21)
        };
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return sub.Id;
    }

    // ── Found by a real Stripe test-mode run (2026-09-24) ─────────────────

    private async Task<Guid> SeedPaid(
        string stripeSubId, SubscriptionStatus status, BillingInterval interval, decimal billed)
    {
        Subscription sub = new()
        {
            StudioId = Guid.NewGuid(),
            StripeSubscriptionId = stripeSubId,
            Status = status,
            BillingInterval = interval,
            BilledUnitAmount = billed,
            BilledQuantity = 1,
            BilledCurrency = "eur",
            PastDueSince = status == SubscriptionStatus.PastDue ? DateTime.UtcNow.AddDays(-2) : null,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(-1),
        };
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return sub.Id;
    }

    [Fact]
    public async Task Handle_PastDueSubscription_RecordsRecoveredAndClearsPastDueSince()
    {
        // invoice.paid and customer.subscription.updated arrive in either order (real Stripe sent
        // invoice.paid first, in the same second). Whichever flips PastDue -> Active must record it.
        await SeedPaid("sub_rec", SubscriptionStatus.PastDue, BillingInterval.Yearly, 790m);

        await CreateSut().Handle(
            Command("sub_rec", DateTime.UtcNow.AddYears(1)) with { StripeEventId = "evt_paid" }, default);

        SubscriptionRevenueEvent ledgerEvent = _db.SubscriptionRevenueEvents.Single();
        ledgerEvent.Type.Should().Be(RevenueEventType.Recovered);
        ledgerEvent.MrrBefore.Should().BeApproximately(65.83m, 0.01m);
        ledgerEvent.MrrAfter.Should().Be(ledgerEvent.MrrBefore);
        ledgerEvent.Source.Should().Be(nameof(HandleInvoicePaidHandler));
        ledgerEvent.StripeEventId.Should().Be("evt_paid");
        _db.Subscriptions.Single(s => s.StripeSubscriptionId == "sub_rec").PastDueSince.Should().BeNull();
    }

    [Theory]
    [InlineData(SubscriptionStatus.Active)]
    [InlineData(SubscriptionStatus.Trialing)]
    public async Task Handle_SubscriptionThatWasNotPastDue_WritesNoLedgerEvent(SubscriptionStatus status)
    {
        await SeedPaid("sub_norec", status, BillingInterval.Monthly, 59m);

        await CreateSut().Handle(Command("sub_norec", DateTime.UtcNow.AddMonths(1)), default);

        _db.SubscriptionRevenueEvents.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_YearlyInvoiceWithoutPaymentIntent_ResolvesItFromStripe()
    {
        // Real payloads never carry it: Invoice.payments is an expandable field Stripe omits.
        await SeedPaid("sub_yr", SubscriptionStatus.Active, BillingInterval.Yearly, 790m);
        _stripe.GetInvoicePaymentIntentIdAsync("in_yr", Arg.Any<CancellationToken>()).Returns("pi_resolved");

        await CreateSut().Handle(Command("sub_yr", DateTime.UtcNow.AddYears(1), "in_yr"), default);

        _db.SubscriptionInvoicePayments.Single(p => p.StripeInvoiceId == "in_yr")
            .StripePaymentIntentId.Should().Be("pi_resolved");
    }

    [Fact]
    public async Task Handle_YearlyInvoiceAlreadyCarryingAPaymentIntent_DoesNotCallStripe()
    {
        await SeedPaid("sub_yr2", SubscriptionStatus.Active, BillingInterval.Yearly, 790m);

        await CreateSut().Handle(
            Command("sub_yr2", DateTime.UtcNow.AddYears(1), "in_yr2") with { StripePaymentIntentId = "pi_from_payload" },
            default);

        _db.SubscriptionInvoicePayments.Single().StripePaymentIntentId.Should().Be("pi_from_payload");
        await _stripe.DidNotReceive().GetInvoicePaymentIntentIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MonthlyInvoice_NeverPaysForTheExtraStripeCall()
    {
        // Only Yearly invoices can be refunded, so only they need the PaymentIntent.
        await SeedPaid("sub_mo", SubscriptionStatus.Active, BillingInterval.Monthly, 59m);

        await CreateSut().Handle(Command("sub_mo", DateTime.UtcNow.AddMonths(1), "in_mo"), default);

        await _stripe.DidNotReceive().GetInvoicePaymentIntentIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _db.SubscriptionInvoicePayments.Single().StripePaymentIntentId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_StripeLookupFails_StillRecordsThePaymentAndActivates()
    {
        await SeedPaid("sub_fail", SubscriptionStatus.PastDue, BillingInterval.Yearly, 790m);
        _stripe.GetInvoicePaymentIntentIdAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("stripe unavailable"));

        Func<Task> act = () => CreateSut().Handle(Command("sub_fail", DateTime.UtcNow.AddYears(1), "in_fail"), default);

        await act.Should().NotThrowAsync();
        _db.SubscriptionInvoicePayments.Single().StripePaymentIntentId.Should().BeNull();
        _db.Subscriptions.Single(s => s.StripeSubscriptionId == "sub_fail").Status.Should().Be(SubscriptionStatus.Active);
    }
}
