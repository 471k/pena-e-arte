using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Pena_e_Arte.Application.Billing;
using Pena_e_Arte.Application.Billing.Commands;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Billing;

public class LatestInvoiceRecorderTests
{
    private readonly IStripeBillingService _billing = Substitute.For<IStripeBillingService>();
    private readonly ISender _sender = Substitute.For<ISender>();

    private static readonly DateTime PaidAt = new(2026, 9, 24, 21, 48, 57, DateTimeKind.Utc);
    private static readonly DateTime PeriodStart = new(2026, 9, 24, 21, 48, 57, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2027, 9, 24, 21, 48, 57, DateTimeKind.Utc);

    private Task Record() =>
        LatestInvoiceRecorder.RecordAsync(
            _billing, _sender, NullLogger.Instance, "sub_new", default);

    [Fact]
    public async Task PaidInvoiceFound_IsRecordedThroughHandleInvoicePaid_WithAllTheResolvedFacts()
    {
        _billing.GetLatestPaidInvoiceAsync("sub_new", Arg.Any<CancellationToken>()).Returns(
            new StripeInvoiceInfo("in_first", 790m, 10m, "eur", PaidAt, PeriodStart, PeriodEnd, "pi_first"));

        await Record();

        await _sender.Received(1).Send(
            Arg.Is<HandleInvoicePaidCommand>(c =>
                c.StripeSubscriptionId == "sub_new"
                && c.StripeInvoiceId == "in_first"
                && c.AmountPaid == 790m
                && c.DiscountAmount == 10m
                && c.Currency == "eur"
                && c.PaidAt == PaidAt
                && c.PeriodStart == PeriodStart
                && c.PeriodEnd == PeriodEnd
                && c.StripePaymentIntentId == "pi_first"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NoPaidInvoiceYet_SendsNothing()
    {
        _billing.GetLatestPaidInvoiceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((StripeInvoiceInfo?)null);

        await Record();

        await _sender.DidNotReceive().Send(Arg.Any<HandleInvoicePaidCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StripeLookupFails_DoesNotThrow_SoActivationIsNeverFailed()
    {
        _billing.GetLatestPaidInvoiceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("stripe unavailable"));

        Func<Task> act = Record;

        await act.Should().NotThrowAsync();
        await _sender.DidNotReceive().Send(Arg.Any<HandleInvoicePaidCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecordingFails_DoesNotThrow()
    {
        _billing.GetLatestPaidInvoiceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(
            new StripeInvoiceInfo("in_x", 59m, 0m, "eur", PaidAt, PeriodStart, PeriodEnd, null));
        _sender.Send(Arg.Any<HandleInvoicePaidCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db down"));

        Func<Task> act = Record;

        await act.Should().NotThrowAsync();
    }
}
