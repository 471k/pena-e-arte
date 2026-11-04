using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class GetPaymentByAppointmentHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _studioId = Guid.NewGuid();

    public GetPaymentByAppointmentHandlerTests() => _currentUser.Role.Returns("owner");

    private GetPaymentByAppointmentHandler CreateSut() => new(_db, _currentUser);

    [Fact]
    public async Task Handle_ExistingPayment_ReturnsPaymentResponse()
    {
        Guid appointmentId = Guid.NewGuid();
        await SeedPayment(200m, appointmentId);

        PaymentResponse? result = await CreateSut()
            .Handle(new GetPaymentByAppointmentQuery(appointmentId), default);

        result.Should().NotBeNull();
        result!.AppointmentId.Should().Be(appointmentId);
        result.Amount.Should().Be(200m);
    }

    // The owner's Payment page names the booking by client and date instead of its raw id.
    [Fact]
    public async Task Handle_ExistingAppointment_ReturnsItsDateAndStatus()
    {
        DateTime date = new(2026, 11, 4, 10, 0, 0, DateTimeKind.Utc);
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ClientId = Guid.NewGuid(),
            Date = date,
            EndDate = date.AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Confirmed,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        await SeedPayment(100m, appointment.Id);

        PaymentResponse? result = await CreateSut()
            .Handle(new GetPaymentByAppointmentQuery(appointment.Id), default);

        result!.AppointmentDate.Should().Be(date);
        result.AppointmentStatus.Should().Be("Confirmed");
    }

    [Fact]
    public async Task Handle_NoPaymentForAppointment_ReturnsNull()
    {
        PaymentResponse? result = await CreateSut()
            .Handle(new GetPaymentByAppointmentQuery(Guid.NewGuid()), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_PaymentWithSplits_ReturnsSplitsInResponse()
    {
        Guid appointmentId = Guid.NewGuid();
        Guid paymentId = await SeedPayment(300m, appointmentId);

        _db.SessionSplits.Add(new SessionSplit
        {
            StudioId = _studioId,
            PaymentId = paymentId,
            Label = "Deposit",
            Amount = 300m
        });
        await _db.SaveChangesAsync();

        PaymentResponse? result = await CreateSut()
            .Handle(new GetPaymentByAppointmentQuery(appointmentId), default);

        result!.AppointmentId.Should().Be(appointmentId);
        result.Amount.Should().Be(300m);
        result.Splits.Should().ContainSingle(s => s.Label == "Deposit" && s.Amount == 300m);
    }

    [Fact]
    public async Task Handle_PaymentWithNoSplits_ReturnsEmptySplitsList()
    {
        Guid appointmentId = Guid.NewGuid();
        await SeedPayment(300m, appointmentId);

        PaymentResponse? result = await CreateSut()
            .Handle(new GetPaymentByAppointmentQuery(appointmentId), default);

        result!.Splits.Should().BeEmpty();
    }

    // ── Artist scope: only a session assigned to the caller ─────────────────────

    [Fact]
    public async Task Artist_OwnAssignedSession_GetsThePayment()
    {
        Guid artistUserId = Guid.NewGuid();
        Guid artistId = await SeedArtist(artistUserId);
        Guid appointmentId = await SeedAppointment(artistId);
        await SeedPayment(50m, appointmentId);

        PaymentResponse? result = await AsArtist(artistUserId).Handle(new GetPaymentByAppointmentQuery(appointmentId), default);

        result.Should().NotBeNull();
        result!.AppointmentId.Should().Be(appointmentId);
    }

    [Fact]
    public async Task Artist_AnotherArtistsSession_GetsNothing_SoNoSplitsLeak()
    {
        Guid artistUserId = Guid.NewGuid();
        await SeedArtist(artistUserId);
        Guid colleague = await SeedArtist(Guid.NewGuid());
        Guid appointmentId = await SeedAppointment(colleague);
        Guid paymentId = await SeedPayment(300m, appointmentId);
        _db.SessionSplits.Add(new SessionSplit { StudioId = _studioId, PaymentId = paymentId, Label = "Colleague", Amount = 300m });
        await _db.SaveChangesAsync();

        PaymentResponse? result = await AsArtist(artistUserId).Handle(new GetPaymentByAppointmentQuery(appointmentId), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Artist_UnassignedRequest_GetsNothing()
    {
        Guid artistUserId = Guid.NewGuid();
        await SeedArtist(artistUserId);
        Guid appointmentId = await SeedAppointment(artistId: null);
        await SeedPayment(50m, appointmentId);

        (await AsArtist(artistUserId).Handle(new GetPaymentByAppointmentQuery(appointmentId), default)).Should().BeNull();
    }

    [Fact]
    public async Task Artist_WithNoArtistProfile_GetsNothing()
    {
        Guid appointmentId = await SeedAppointment(await SeedArtist(Guid.NewGuid()));
        await SeedPayment(50m, appointmentId);

        (await AsArtist(Guid.NewGuid()).Handle(new GetPaymentByAppointmentQuery(appointmentId), default)).Should().BeNull();
    }

    [Fact]
    public async Task Owner_StillSeesAnyArtistsSession()
    {
        Guid appointmentId = await SeedAppointment(await SeedArtist(Guid.NewGuid()));
        await SeedPayment(50m, appointmentId);

        (await CreateSut().Handle(new GetPaymentByAppointmentQuery(appointmentId), default)).Should().NotBeNull();
    }

    [Fact]
    public void Validator_RejectsAnEmptyAppointmentId()
    {
        new GetPaymentByAppointmentValidator().Validate(new GetPaymentByAppointmentQuery(Guid.Empty))
            .IsValid.Should().BeFalse();
    }

    private GetPaymentByAppointmentHandler AsArtist(Guid userId)
    {
        ICurrentUser artist = Substitute.For<ICurrentUser>();
        artist.Role.Returns("artist");
        artist.UserId.Returns(userId);
        return new GetPaymentByAppointmentHandler(_db, artist);
    }

    private async Task<Guid> SeedArtist(Guid userId)
    {
        Artist artist = new() { StudioId = _studioId, UserId = userId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid():N}@a.test" };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist.Id;
    }

    private async Task<Guid> SeedAppointment(Guid? artistId)
    {
        DateTime start = DateTime.UtcNow.AddDays(3);
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ClientId = Guid.NewGuid(),
            ArtistId = artistId,
            Date = start,
            EndDate = start.AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Confirmed,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return appointment.Id;
    }

    private async Task<Guid> SeedPayment(decimal amount, Guid? appointmentId = null)
    {
        Client client = new()
        {
            StudioId = _studioId,
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = appointmentId ?? Guid.NewGuid(),
            ClientId = client.Id,
            Amount = amount,
            Status = PaymentStatus.Pending
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }
}
