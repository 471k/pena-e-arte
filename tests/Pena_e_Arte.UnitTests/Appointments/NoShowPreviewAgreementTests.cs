using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

/// <summary>
/// E04b (A04): for every deposit state, the no-show preview names exactly the outcome the commit then
/// applies and reports, and the commit's writes are the ones MarkNoShowHandler made before the decision
/// was extracted. The declared-cash case is asserted AS IT BEHAVES TODAY (prompt §2.1): recorded as Paid
/// without PaidAt. That is flagged for Phi, not endorsed.
/// </summary>
public class NoShowPreviewAgreementTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly IPaymentProvider _provider = Substitute.For<IPaymentProvider>();
    private readonly Guid _studioId = Guid.NewGuid();

    public NoShowPreviewAgreementTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _db.Studios.Add(new Studio { Id = _studioId, Name = "S", Slug = _studioId.ToString("N")[..8], Currency = "EUR" });
        _db.SaveChanges();
    }

    public static TheoryData<string, PaymentStatus?, ClientPaymentMethod, string?, string, bool> States() => new()
    {
        // label, payment status (null = no payment row), method, provider ref, expected outcome, provider capture?
        { "no payment",          null,                       ClientPaymentMethod.Card, null,       "NothingCollected",           false },
        { "unpaid card intent",  PaymentStatus.Pending,      ClientPaymentMethod.Card, "pok_1",    "NothingCollected",           false },
        { "failed card",         PaymentStatus.Failed,       ClientPaymentMethod.Card, "pok_1",    "NothingCollected",           false },
        { "charged card",        PaymentStatus.Paid,         ClientPaymentMethod.Card, "pok_1",    "ChargedDepositKept",         false },
        { "cash received",       PaymentStatus.Paid,         ClientPaymentMethod.Cash, null,       "CashDepositKept",            false },
        { "legacy held card",    PaymentStatus.Captured,     ClientPaymentMethod.Card, "pok_held", "HeldDepositCaptured",        true  },
        { "held, no reference",  PaymentStatus.Captured,     ClientPaymentMethod.Card, null,       "Unknown",                    false },
        { "declared cash (§2.1)", PaymentStatus.CashPending, ClientPaymentMethod.Cash, null,       "DeclaredCashRecordedAsPaid", false },
    };

    [Theory]
    [MemberData(nameof(States))]
    public async Task ThePreview_NamesWhatTheCommitDoes(
        string label, PaymentStatus? status, ClientPaymentMethod method, string? reference, string expected, bool captures)
    {
        Guid id = SeedAppointment(AppointmentStatus.Confirmed);
        Payment? payment = status is null ? null : SeedPayment(id, status.Value, method, reference);
        PaymentStatus? before = payment?.Status;
        DateTime? paidAtBefore = payment?.PaidAt;

        NoShowPreviewResponse preview = await new GetNoShowPreviewHandler(_db, FakeCurrentUser.Owner()).Handle(new GetNoShowPreviewQuery(id), default);
        NoShowResultResponse result = await new MarkNoShowHandler(_db, _tenant, Substitute.For<IRealtimeNotifier>(), _provider, FakeCurrentUser.Owner())
            .Handle(new MarkNoShowCommand(id), default);

        preview.CanMarkNoShow.Should().BeTrue(label);
        preview.Outcome.Should().Be(expected, label);
        result.Outcome.Should().Be(preview.Outcome, "the commit applies the decision the preview named");
        result.Amount.Should().Be(preview.Amount);
        result.Currency.Should().Be(preview.Currency).And.Be("EUR");
        preview.Amount.Should().Be(expected is "NothingCollected" ? 0m : 80m);

        // The provider is called only for a capturable hold, exactly once.
        if (captures)
            await _provider.Received(1).CaptureAsync(_studioId, "pok_held", Arg.Any<CancellationToken>());
        else
            await _provider.DidNotReceiveWithAnyArgs().CaptureAsync(default, default!, default);

        // The writes, unchanged from before the decision was extracted.
        Appointment after = _db.Appointments.Single(a => a.Id == id);
        after.Status.Should().Be(AppointmentStatus.NoShow);
        after.DepositStatus.Should().Be(DepositStatus.Forfeited);
        if (payment is not null)
        {
            Payment stored = _db.Payments.Single(p => p.Id == payment.Id);
            switch (expected)
            {
                case "HeldDepositCaptured":
                    stored.Status.Should().Be(PaymentStatus.Paid);
                    stored.PaidAt.Should().NotBeNull();
                    break;
                case "DeclaredCashRecordedAsPaid":
                    // §2.1, as the code behaves today: Paid, and PaidAt still unset.
                    stored.Status.Should().Be(PaymentStatus.Paid);
                    stored.PaidAt.Should().BeNull();
                    break;
                default:
                    stored.Status.Should().Be(before!.Value);
                    stored.PaidAt.Should().Be(paidAtBefore);
                    break;
            }
            result.PaymentStatus.Should().Be(stored.Status.ToString());
        }
        else
        {
            result.PaymentStatus.Should().BeNull();
        }
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    public async Task AClosedAppointment_IsRefusedByBoth_InTheSameWords(AppointmentStatus status)
    {
        Guid id = SeedAppointment(status);

        NoShowPreviewResponse preview = await new GetNoShowPreviewHandler(_db, FakeCurrentUser.Owner()).Handle(new GetNoShowPreviewQuery(id), default);
        Func<Task> commit = () => new MarkNoShowHandler(_db, _tenant, Substitute.For<IRealtimeNotifier>(), _provider, FakeCurrentUser.Owner())
            .Handle(new MarkNoShowCommand(id), default);

        preview.CanMarkNoShow.Should().BeFalse();
        (await commit.Should().ThrowAsync<BusinessRuleViolationException>()).Which.Message.Should().Be(preview.Message);
    }

    [Fact]
    public async Task TheRefundedRowIsIgnored_ByBoth()
    {
        // Both read "the first payment that isn't Refunded", the same lookup.
        Guid id = SeedAppointment(AppointmentStatus.Confirmed);
        SeedPayment(id, PaymentStatus.Refunded, ClientPaymentMethod.Card, "pok_old");

        NoShowPreviewResponse preview = await new GetNoShowPreviewHandler(_db, FakeCurrentUser.Owner()).Handle(new GetNoShowPreviewQuery(id), default);

        preview.Outcome.Should().Be("NothingCollected");
    }

    [Fact]
    public async Task AMissingAppointment_IsNotFound()
    {
        Func<Task> act = () => new GetNoShowPreviewHandler(_db, FakeCurrentUser.Owner()).Handle(new GetNoShowPreviewQuery(Guid.NewGuid()), default);
        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task TheValidator_RejectsAnEmptyId()
    {
        (await new GetNoShowPreviewValidator().ValidateAsync(new GetNoShowPreviewQuery(Guid.Empty))).IsValid.Should().BeFalse();
    }

    private Guid SeedAppointment(AppointmentStatus status)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            Date = DateTime.UtcNow.AddHours(-2),
            EndDate = DateTime.UtcNow.AddHours(-1),
            DurationMinutes = 60,
            Status = status,
            DepositStatus = DepositStatus.Pending,
            DepositAmount = 80m,
        };
        _db.Appointments.Add(appointment);
        _db.SaveChanges();
        return appointment.Id;
    }

    private Payment SeedPayment(Guid appointmentId, PaymentStatus status, ClientPaymentMethod method, string? reference)
    {
        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = appointmentId,
            ClientId = Guid.NewGuid(),
            Amount = 80m,
            Currency = "EUR",
            Status = status,
            Method = method,
            ProviderReferenceId = reference,
            PaidAt = status == PaymentStatus.Paid ? DateTime.UtcNow.AddDays(-3) : null,
        };
        _db.Payments.Add(payment);
        _db.SaveChanges();
        return payment;
    }
}
