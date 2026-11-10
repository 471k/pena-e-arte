using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

/// <summary>
/// E05a (O01): the owner's dashboard exceptions. Each source in isolation (none, one, several), the
/// counts being real counts rather than the length of a page, and the stated ordering rule.
/// </summary>
public class GetOwnerExceptionsHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _clientId;
    private readonly Guid _artistId;

    public GetOwnerExceptionsHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _db.Studios.Add(new Studio { Id = _studioId, Name = "Ink", Slug = _studioId.ToString("N")[..8], OwnerEmail = "o@ink.test", Currency = "EUR" });
        Client client = new() { StudioId = _studioId, FirstName = "Mira", LastName = "Hoxha", Email = "m@c.test" };
        Artist artist = new() { StudioId = _studioId, FirstName = "Ana", LastName = "Costa", Email = "a@a.test" };
        _db.Clients.Add(client);
        _db.Artists.Add(artist);
        _db.SaveChanges();
        _clientId = client.Id;
        _artistId = artist.Id;
    }

    private Task<OwnerExceptionsResponse> Read() =>
        new GetOwnerExceptionsHandler(_db, _tenant, new FixedTimeProvider(Now))
            .Handle(new GetOwnerExceptionsQuery(), default);

    private static OwnerExceptionGroupResponse? Group(OwnerExceptionsResponse r, string kind) =>
        r.Groups.SingleOrDefault(g => g.Kind == kind);

    // ── nothing to act on ─────────────────────────────────────────────────────

    [Fact]
    public async Task AQuietStudio_HasNoGroupsAtAll()
    {
        // A confirmed, assigned, paid session with nothing required: nothing for the owner to do.
        await Appt(hoursFromNow: 24, deposit: DepositStatus.Paid);

        OwnerExceptionsResponse r = await Read();

        r.Groups.Should().BeEmpty();
        r.WindowDays.Should().Be(7);
    }

    // ── unassigned ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Unassigned_ListsUpcomingRequestsWithNoArtist_SoonestFirst()
    {
        Guid later = await Appt(hoursFromNow: 72, artist: false, status: AppointmentStatus.Pending, deposit: DepositStatus.Paid);
        Guid sooner = await Appt(hoursFromNow: 5, artist: false, status: AppointmentStatus.Pending, deposit: DepositStatus.Paid);
        await Appt(hoursFromNow: -5, artist: false, status: AppointmentStatus.Pending, deposit: DepositStatus.Paid); // past: not here
        await Appt(hoursFromNow: 10, artist: false, status: AppointmentStatus.Cancelled, deposit: DepositStatus.Paid);

        OwnerExceptionGroupResponse g = Group(await Read(), OwnerExceptionKinds.Unassigned)!;

        g.Total.Should().Be(2);
        g.Items.Select(i => i.AppointmentId).Should().Equal(sooner, later);
        g.Items.Should().OnlyContain(i => i.ArtistName == null && i.ClientName == "Mira Hoxha");
    }

    [Fact]
    public async Task Unassigned_TotalIsTheRealCount_WhenThereAreMoreThanTheItemsCarried()
    {
        for (int i = 0; i < GetOwnerExceptionsHandler.MaxItemsPerGroup + 3; i++)
            await Appt(hoursFromNow: 24 * 10 + i, artist: false, status: AppointmentStatus.Pending, deposit: DepositStatus.Paid);

        OwnerExceptionGroupResponse g = Group(await Read(), OwnerExceptionKinds.Unassigned)!;

        g.Total.Should().Be(GetOwnerExceptionsHandler.MaxItemsPerGroup + 3);
        g.Items.Should().HaveCount(GetOwnerExceptionsHandler.MaxItemsPerGroup);
    }

    // ── awaiting confirmation ─────────────────────────────────────────────────

    [Fact]
    public async Task AwaitingConfirmation_IsPendingWithAnArtist_AndNeverRepeatsAnUnassignedRequest()
    {
        Guid pending = await Appt(hoursFromNow: 30, status: AppointmentStatus.Pending, deposit: DepositStatus.Paid);
        await Appt(hoursFromNow: 31, artist: false, status: AppointmentStatus.Pending, deposit: DepositStatus.Paid);
        await Appt(hoursFromNow: 32, status: AppointmentStatus.Confirmed, deposit: DepositStatus.Paid);

        OwnerExceptionsResponse r = await Read();

        OwnerExceptionGroupResponse g = Group(r, OwnerExceptionKinds.AwaitingConfirmation)!;
        g.Total.Should().Be(1);
        g.Items.Single().AppointmentId.Should().Be(pending);
        g.Items.Single().ArtistName.Should().Be("Ana Costa");
        Group(r, OwnerExceptionKinds.Unassigned)!.Items.Should().NotContain(i => i.AppointmentId == pending);
    }

    // ── cash to confirm ───────────────────────────────────────────────────────

    [Fact]
    public async Task CashToConfirm_ListsEveryCashPromise_WhateverTheDate_WithThePayment()
    {
        Guid farOut = await Appt(hoursFromNow: 24 * 40, deposit: DepositStatus.Pending);
        Guid past = await Appt(hoursFromNow: -48, deposit: DepositStatus.Pending);
        await Pay(farOut, PaymentStatus.CashPending, ClientPaymentMethod.Cash);
        await Pay(past, PaymentStatus.CashPending, ClientPaymentMethod.Cash);

        OwnerExceptionGroupResponse g = Group(await Read(), OwnerExceptionKinds.CashToConfirm)!;

        g.Total.Should().Be(2);
        g.Items.Select(i => i.AppointmentId).Should().Equal(past, farOut);
        g.Items.Should().OnlyContain(i =>
            i.Deposit!.Payment!.Status == "CashPending" && i.Deposit.Payment.ClientToken == null && i.Deposit.Payment.Splits!.Count == 0);
    }

    // ── unpaid deposits ───────────────────────────────────────────────────────

    [Fact]
    public async Task UnpaidDeposit_CoversNotStartedPendingAndFailed_InTheWindowOnly()
    {
        Guid notStarted = await Appt(hoursFromNow: 24, deposit: DepositStatus.Pending);
        Guid cardPending = await Appt(hoursFromNow: 48, deposit: DepositStatus.Pending);
        Guid failed = await Appt(hoursFromNow: 72, deposit: DepositStatus.Pending);
        Guid cash = await Appt(hoursFromNow: 96, deposit: DepositStatus.Pending);
        Guid held = await Appt(hoursFromNow: 100, deposit: DepositStatus.Pending);
        await Appt(hoursFromNow: 24 * 8, deposit: DepositStatus.Pending);          // outside 7 days
        await Appt(hoursFromNow: 30, deposit: DepositStatus.Pending, amount: 0m); // nothing due
        await Pay(cardPending, PaymentStatus.Pending, ClientPaymentMethod.Card);
        await Pay(failed, PaymentStatus.Failed, ClientPaymentMethod.Card);
        await Pay(cash, PaymentStatus.CashPending, ClientPaymentMethod.Cash);
        await Pay(held, PaymentStatus.Captured, ClientPaymentMethod.Card);

        OwnerExceptionsResponse r = await Read();
        OwnerExceptionGroupResponse g = Group(r, OwnerExceptionKinds.UnpaidDeposit)!;

        g.Total.Should().Be(3);
        g.Items.Select(i => i.AppointmentId).Should().Equal(notStarted, cardPending, failed);
        g.Items[0].Deposit.Should().Be(new OwnerExceptionDepositResponse(50m, "EUR", null), "no payment exists yet");
        g.Items[1].Deposit!.Payment!.Status.Should().Be("Pending");
        g.Items[2].Deposit!.Payment!.Status.Should().Be("Failed");
        Group(r, OwnerExceptionKinds.CashToConfirm)!.Items.Single().AppointmentId.Should().Be(cash);
    }

    // ── preparation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Preparation_ListsSessionsInTheWindowWithSomethingOutstanding_WithTheSummary()
    {
        Guid serviceId = await Service(consent: true, intake: true);
        Guid unprepared = await Appt(hoursFromNow: 24, deposit: DepositStatus.Paid, serviceId: serviceId);
        Guid prepared = await Appt(hoursFromNow: 48, deposit: DepositStatus.Paid, serviceId: serviceId);
        await Appt(hoursFromNow: 24 * 9, deposit: DepositStatus.Paid, serviceId: serviceId); // outside 7 days
        _db.ConsentForms.Add(new ConsentForm { StudioId = _studioId, ClientId = _clientId, AppointmentId = prepared, SignedAt = Now.UtcDateTime, SignatureData = "M H" });
        _db.IntakeForms.Add(new IntakeForm { StudioId = _studioId, ClientId = _clientId, AppointmentId = prepared, SubmittedAt = Now.UtcDateTime });
        _db.ConsentForms.Add(new ConsentForm { StudioId = _studioId, ClientId = _clientId, AppointmentId = unprepared, SignedAt = Now.UtcDateTime, SignatureData = "M H" });
        await _db.SaveChangesAsync();

        OwnerExceptionGroupResponse g = Group(await Read(), OwnerExceptionKinds.Preparation)!;

        g.Total.Should().Be(1);
        g.Items.Single().AppointmentId.Should().Be(unprepared);
        g.Items.Single().Readiness.Should().Be(new AppointmentReadinessSummaryResponse(2, 1));
    }

    // ── ordering ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Groups_AreOrderedBySoonestAppointment_OverdueCashFirst()
    {
        await Appt(hoursFromNow: 50, status: AppointmentStatus.Pending, deposit: DepositStatus.Paid);   // awaiting, +50h
        await Appt(hoursFromNow: 10, artist: false, status: AppointmentStatus.Pending, deposit: DepositStatus.Paid); // unassigned, +10h
        Guid overdue = await Appt(hoursFromNow: -24, deposit: DepositStatus.Pending);
        await Pay(overdue, PaymentStatus.CashPending, ClientPaymentMethod.Cash);                       // cash, -24h

        OwnerExceptionsResponse r = await Read();

        r.Groups.Select(g => g.Kind).Should().Equal(
            OwnerExceptionKinds.CashToConfirm, OwnerExceptionKinds.Unassigned, OwnerExceptionKinds.AwaitingConfirmation);
    }

    [Fact]
    public async Task GroupsWithTheSameSoonestAppointment_KeepTheFixedKindOrder()
    {
        // One unassigned request with an unpaid deposit and no service: Unassigned and UnpaidDeposit
        // share the same soonest appointment.
        await Appt(hoursFromNow: 20, artist: false, status: AppointmentStatus.Pending, deposit: DepositStatus.Pending);

        OwnerExceptionsResponse r = await Read();

        r.Groups.Select(g => g.Kind).Should().Equal(OwnerExceptionKinds.Unassigned, OwnerExceptionKinds.UnpaidDeposit);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<Guid> Appt(
        double hoursFromNow,
        DepositStatus deposit,
        bool artist = true,
        AppointmentStatus status = AppointmentStatus.Confirmed,
        decimal amount = 50m,
        Guid? serviceId = null)
    {
        DateTime start = Now.UtcDateTime.AddHours(hoursFromNow);
        Appointment a = new()
        {
            StudioId = _studioId,
            ClientId = _clientId,
            ArtistId = artist ? _artistId : null,
            ServiceId = serviceId,
            Date = start,
            EndDate = start.AddHours(2),
            DurationMinutes = 120,
            Status = status,
            DepositStatus = deposit,
            DepositAmount = amount,
        };
        _db.Appointments.Add(a);
        await _db.SaveChangesAsync();
        return a.Id;
    }

    private async Task Pay(Guid appointmentId, PaymentStatus status, ClientPaymentMethod method)
    {
        _db.Payments.Add(new Payment
        {
            StudioId = _studioId,
            AppointmentId = appointmentId,
            ClientId = _clientId,
            Amount = 50m,
            Status = status,
            Method = method,
            Currency = "EUR",
            ClientToken = "secret-token",
        });
        await _db.SaveChangesAsync();
    }

    private async Task<Guid> Service(bool consent, bool intake)
    {
        Service s = new()
        {
            StudioId = _studioId,
            Name = "Session",
            DurationMinutes = 120,
            IsActive = true,
            RequiresConsentForm = consent,
            RequiresIntakeForm = intake,
        };
        _db.Services.Add(s);
        await _db.SaveChangesAsync();
        return s.Id;
    }
}
