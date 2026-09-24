using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Billing.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class GetCancellationQuoteHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public GetCancellationQuoteHandlerTests() => _tenant.StudioId.Returns(_studioId);

    private GetCancellationQuoteHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_Monthly_ReturnsZeroRefundQuote()
    {
        await SeedSubscription(BillingInterval.Monthly, "sub_monthly");

        CancellationQuoteResponse result = await CreateSut().Handle(new GetCancellationQuoteQuery(), default);

        result.BillingInterval.Should().Be("Monthly");
        result.RefundAmount.Should().Be(0m);
        result.MonthsUsed.Should().BeNull();
    }

    [Fact]
    public async Task Handle_CashBilled_ReturnsZeroRefundQuote()
    {
        await SeedSubscription(BillingInterval.Yearly, stripeId: null);

        CancellationQuoteResponse result = await CreateSut().Handle(new GetCancellationQuoteQuery(), default);

        // Cash-billed is always Monthly in this system (§3 flag) — reported as such.
        result.BillingInterval.Should().Be("Monthly");
        result.RefundAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_YearlyWithSnapshottedInvoice_ReturnsFormulaQuote()
    {
        const string stripeId = "sub_yearly_quote";
        Guid subId = await SeedSubscription(BillingInterval.Yearly, stripeId);
        _db.SubscriptionInvoicePayments.Add(new SubscriptionInvoicePayment
        {
            SubscriptionId = subId,
            StudioId = _studioId,
            StripeInvoiceId = "in_1",
            AmountPaid = 790m,
            Currency = "eur",
            // +5 minutes — see CancelMySubscriptionHandlerTests.SeedYearly's comment: guards
            // against the exact-anniversary boundary tipping MonthsUsed by one.
            PaidAt = DateTime.UtcNow.AddMonths(-3).AddMinutes(5),
            PeriodStart = DateTime.UtcNow.AddMonths(-3).AddMinutes(5),
            MonthlyReferencePrice = 79m,
            StripePaymentIntentId = "pi_1",
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();

        CancellationQuoteResponse result = await CreateSut().Handle(new GetCancellationQuoteQuery(), default);

        result.BillingInterval.Should().Be("Yearly");
        result.MonthsUsed.Should().Be(3);
        result.RefundAmount.Should().Be(553m);
    }

    [Fact]
    public async Task Handle_YearlyWithNoInvoice_ReturnsZeroRefundQuote()
    {
        await SeedSubscription(BillingInterval.Yearly, "sub_yearly_noinvoice");

        CancellationQuoteResponse result = await CreateSut().Handle(new GetCancellationQuoteQuery(), default);

        result.BillingInterval.Should().Be("Yearly");
        result.RefundAmount.Should().Be(0m);
        result.MonthsUsed.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NoSubscription_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetCancellationQuoteQuery(), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> SeedSubscription(BillingInterval interval, string? stripeId)
    {
        Subscription sub = new()
        {
            StudioId = _studioId,
            Status = SubscriptionStatus.Active,
            BillingInterval = interval,
            StripeSubscriptionId = stripeId,
            TrialExpiresAt = null,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(10),
            GracePeriodEnd = DateTime.UtcNow.AddDays(-13),
        };
        _db.Subscriptions.Add(sub);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return sub.Id;
    }
}
