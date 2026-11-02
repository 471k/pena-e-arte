using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// The cancellation preview and the real cancellation share one decision (CancellationOutcome), so
/// what a client is shown must be what then happens — checked here against real MySQL for every
/// payment-status branch, and for a rule that changes between preview and commit (the commit wins).
/// </summary>
[Collection("Database")]
public class CancellationPreviewIntegrationTests(DatabaseFixture fixture)
{
    private readonly IPaymentProvider _provider = Substitute.For<IPaymentProvider>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();

    public static TheoryData<string, PaymentStatus, ClientPaymentMethod, int, bool, CancellationClientOutcome, CancellationProviderAction> Branches => new()
    {
        // name, payment status, method, late refund %, enough notice?, expected outcome, expected provider call
        // (every rule: 72 h window)
        { "held, full",       PaymentStatus.Captured,    ClientPaymentMethod.Card, 25, true,  CancellationClientOutcome.FullyRefunded,     CancellationProviderAction.ReleaseHold },
        { "held, partial",    PaymentStatus.Captured,    ClientPaymentMethod.Card, 25, false, CancellationClientOutcome.PartiallyRefunded, CancellationProviderAction.CaptureKeptShare },
        { "held, kept",       PaymentStatus.Captured,    ClientPaymentMethod.Card, 0,  false, CancellationClientOutcome.DepositKept,       CancellationProviderAction.CaptureWhole },
        { "charged, full",    PaymentStatus.Paid,        ClientPaymentMethod.Card, 25, true,  CancellationClientOutcome.FullyRefunded,     CancellationProviderAction.Refund },
        { "charged, partial", PaymentStatus.Paid,        ClientPaymentMethod.Card, 25, false, CancellationClientOutcome.PartiallyRefunded, CancellationProviderAction.Refund },
        { "charged, kept",    PaymentStatus.Paid,        ClientPaymentMethod.Card, 0,  false, CancellationClientOutcome.DepositKept,       CancellationProviderAction.None },
        { "cash promised",    PaymentStatus.CashPending, ClientPaymentMethod.Cash, 0,  false, CancellationClientOutcome.NothingCollected,  CancellationProviderAction.None },
        { "cash paid",        PaymentStatus.Paid,        ClientPaymentMethod.Cash, 0,  false, CancellationClientOutcome.SettledWithStudio, CancellationProviderAction.None },
        { "card unfinished",  PaymentStatus.Pending,     ClientPaymentMethod.Card, 0,  false, CancellationClientOutcome.NothingCollected,  CancellationProviderAction.None },
        { "card failed",      PaymentStatus.Failed,      ClientPaymentMethod.Card, 0,  false, CancellationClientOutcome.NothingCollected,  CancellationProviderAction.None },
    };

    [Theory]
    [MemberData(nameof(Branches))]
    public async Task PreviewedRefund_EqualsWhatTheCancellationDoes(
        string _, PaymentStatus status, ClientPaymentMethod method, int latePercent, bool enoughNotice,
        CancellationClientOutcome expectedOutcome, CancellationProviderAction expectedAction)
    {
        Seeded s = await Seed(latePercent, enoughNotice ? TimeSpan.FromDays(7) : TimeSpan.FromHours(24));
        await SeedPayment(s, status, method);

        await AssertPreviewMatchesCommit(s, expectedOutcome, expectedAction);
    }

    [Fact]
    public async Task NoPayment_PreviewAndCommitBothMoveNothing()
    {
        Seeded s = await Seed(latePercent: 0, TimeSpan.FromHours(24));

        await AssertPreviewMatchesCommit(s, CancellationClientOutcome.NothingCollected, CancellationProviderAction.None);
    }

    [Fact]
    public async Task RuleChangedBetweenPreviewAndCommit_TheCommittedResultWins()
    {
        // Previewed with a 24 h window: 30 h ahead is enough notice → full refund.
        Seeded s = await Seed(latePercent: 0, TimeSpan.FromHours(30), windowHours: 24);
        await SeedPayment(s, PaymentStatus.Paid, ClientPaymentMethod.Card);

        CancellationPreviewResponse preview = await Preview(s);
        preview.Outcome.Should().Be(nameof(CancellationClientOutcome.FullyRefunded));
        preview.RefundAmount.Should().Be(50m);

        // The studio widens its window before the client confirms.
        await using (AppDbContext edit = fixture.CreateDbContext(s.StudioId))
        {
            DepositRule rule = await edit.DepositRules.SingleAsync();
            rule.CancellationWindowHours = 72;
            rule.UpdatedAt = DateTime.UtcNow;
            await edit.SaveChangesAsync();
        }

        await Cancel(s);

        // The commit re-decided: deposit kept, no refund sent, and the client is told so.
        await _provider.DidNotReceiveWithAnyArgs().RefundAsync(default, default!, default, default!, default);
        await using AppDbContext verify = fixture.CreateDbContext(s.StudioId);
        Appointment appointment = await verify.Appointments.SingleAsync(a => a.Id == s.AppointmentId);
        appointment.DepositStatus.Should().Be(DepositStatus.Forfeited);
        Payment payment = await verify.Payments.SingleAsync(p => p.AppointmentId == s.AppointmentId);
        payment.RefundedAmount.Should().BeNull();
        await _sender.Received(1).Send(
            Arg.Is<SendAppointmentCancellationCommand>(c =>
                c.AppointmentId == s.AppointmentId && c.RefundedAmount == null && c.DepositForfeited),
            Arg.Any<CancellationToken>());
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task AssertPreviewMatchesCommit(
        Seeded s, CancellationClientOutcome expectedOutcome, CancellationProviderAction expectedAction)
    {
        CancellationPreviewResponse preview = await Preview(s);
        preview.Outcome.Should().Be(expectedOutcome.ToString());
        preview.IsProjection.Should().BeTrue();

        await Cancel(s);

        await using AppDbContext verify = fixture.CreateDbContext(s.StudioId);
        Appointment appointment = await verify.Appointments.SingleAsync(a => a.Id == s.AppointmentId);
        Payment? payment = await verify.Payments.SingleOrDefaultAsync(p => p.AppointmentId == s.AppointmentId);
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);

        // What went back to the client is what the preview said...
        (payment?.RefundedAmount ?? 0m).Should().Be(preview.RefundAmount);

        // ...and the provider was asked for exactly the previewed amounts, and nothing else.
        const string reference = "pok_order_1";
        switch (expectedAction)
        {
            case CancellationProviderAction.ReleaseHold:
                await _provider.Received(1).CancelAsync(s.StudioId, reference, Arg.Any<CancellationToken>());
                break;
            case CancellationProviderAction.CaptureWhole:
                await _provider.Received(1).CaptureAsync(s.StudioId, reference, Arg.Any<CancellationToken>());
                break;
            case CancellationProviderAction.CaptureKeptShare:
                await _provider.Received(1).CaptureAsync(
                    s.StudioId, reference, preview.KeptAmount, "EUR", Arg.Any<CancellationToken>());
                break;
            case CancellationProviderAction.Refund:
                await _provider.Received(1).RefundAsync(
                    s.StudioId, reference, preview.RefundAmount, "EUR", Arg.Any<CancellationToken>());
                break;
            case CancellationProviderAction.None:
                _provider.ReceivedCalls().Should().BeEmpty();
                break;
        }
        _provider.ReceivedCalls().Should().HaveCountLessThanOrEqualTo(1);

        DepositStatus expectedDeposit = expectedOutcome switch
        {
            CancellationClientOutcome.FullyRefunded or CancellationClientOutcome.PartiallyRefunded => DepositStatus.Refunded,
            CancellationClientOutcome.DepositKept => DepositStatus.Forfeited,
            // A promised cash deposit is closed as Refunded; everything else leaves the deposit alone.
            _ when payment?.Status == PaymentStatus.Refunded => DepositStatus.Refunded,
            _ => DepositStatus.Pending,
        };
        appointment.DepositStatus.Should().Be(expectedDeposit);

        if (expectedOutcome == CancellationClientOutcome.DepositKept)
            preview.KeptAmount.Should().Be(50m);
        if (expectedOutcome == CancellationClientOutcome.PartiallyRefunded)
            (preview.RefundAmount + preview.KeptAmount).Should().Be(50m);
    }

    private async Task<CancellationPreviewResponse> Preview(Seeded s)
    {
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        return await new GetCancellationPreviewHandler(db, _user)
            .Handle(new GetCancellationPreviewQuery(s.AppointmentId), default);
    }

    private async Task Cancel(Seeded s)
    {
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        CurrentTenantService tenant = new();
        tenant.SetTenant(s.StudioId);
        CancelAppointmentHandler handler = new(
            db, tenant, _user, Substitute.For<IRealtimeNotifier>(), _sender, Substitute.For<IJobScheduler>(),
            _provider, NullLogger<CancelAppointmentHandler>.Instance);
        await handler.Handle(new CancelAppointmentCommand(s.AppointmentId), default);
    }

    private sealed record Seeded(Guid StudioId, Guid ClientId, Guid AppointmentId);

    private async Task<Seeded> Seed(int latePercent, TimeSpan ahead, int windowHours = 72)
    {
        Guid studioId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        _user.Role.Returns("client");
        _user.UserId.Returns(userId);

        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        ctx.Studios.Add(new Studio
        {
            Id = studioId,
            Name = "Preview Studio",
            Slug = studioId.ToString("N")[..8],
            CountryCode = "PT",
            Currency = "EUR",
        });
        Artist artist = new() { StudioId = studioId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@a.com" };
        Client client = new() { StudioId = studioId, UserId = userId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid()}@c.com" };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        ctx.DepositRules.Add(new DepositRule
        {
            StudioId = studioId,
            Name = "Standard",
            AmountFixed = 50m,
            IsActive = true,
            CancellationWindowHours = windowHours,
            RefundPercentOnLateCancel = latePercent,
        });
        await ctx.SaveChangesAsync();

        DateTime start = DateTime.UtcNow.Add(ahead);
        Appointment appointment = new()
        {
            StudioId = studioId,
            ArtistId = artist.Id,
            ClientId = client.Id,
            Date = start,
            EndDate = start.AddHours(2),
            DurationMinutes = 120,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Pending,
            DepositAmount = 50m,
        };
        ctx.Appointments.Add(appointment);
        await ctx.SaveChangesAsync();

        return new Seeded(studioId, client.Id, appointment.Id);
    }

    private async Task SeedPayment(Seeded s, PaymentStatus status, ClientPaymentMethod method)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(s.StudioId);
        ctx.Payments.Add(new Payment
        {
            StudioId = s.StudioId,
            AppointmentId = s.AppointmentId,
            ClientId = s.ClientId,
            Amount = 50m,
            Currency = "EUR",
            Status = status,
            Method = method,
            ProviderReferenceId = method == ClientPaymentMethod.Card ? "pok_order_1" : null,
        });
        await ctx.SaveChangesAsync();
    }
}
