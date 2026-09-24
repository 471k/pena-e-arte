using FluentAssertions;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Billing;

public class HandleInvoicePaidHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private HandleInvoicePaidHandler CreateSut() => new(_db);

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
}
