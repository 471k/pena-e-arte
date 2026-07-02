using FluentAssertions;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class GetAppointmentsHandlerTests
{
    private readonly FakeDbContext   _db          = FakeDbContext.Create();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Owner();
    private readonly Guid            _studioId    = Guid.NewGuid();

    private GetAppointmentsHandler CreateSut() => new(_db, _currentUser);

    [Fact]
    public async Task Handle_NoFilter_ReturnsAllAppointmentsOrderedByDate()
    {
        DateTime sooner = DateTime.UtcNow.AddDays(1);
        DateTime later  = DateTime.UtcNow.AddDays(3);
        await SeedAppointment(later);
        await SeedAppointment(sooner);

        List<AppointmentResponse> result = await CreateSut()
            .Handle(new GetAppointmentsQuery(null, null), default);

        result.Should().HaveCount(2);
        result[0].Date.Should().Be(sooner);
        result[1].Date.Should().Be(later);
    }

    [Fact]
    public async Task Handle_FromFilter_ExcludesEarlierAppointments()
    {
        DateTime early  = DateTime.UtcNow.AddDays(2);
        DateTime late   = DateTime.UtcNow.AddDays(7);
        DateTime cutoff = DateTime.UtcNow.AddDays(5);
        await SeedAppointment(early);
        await SeedAppointment(late);

        List<AppointmentResponse> result = await CreateSut()
            .Handle(new GetAppointmentsQuery(From: cutoff, To: null), default);

        result.Should().ContainSingle(a => a.Date == late);
        result.Should().NotContain(a => a.Date == early);
    }

    [Fact]
    public async Task Handle_ToFilter_ExcludesLaterAppointments()
    {
        DateTime early  = DateTime.UtcNow.AddDays(2);
        DateTime late   = DateTime.UtcNow.AddDays(7);
        DateTime cutoff = DateTime.UtcNow.AddDays(5);
        await SeedAppointment(early);
        await SeedAppointment(late);

        List<AppointmentResponse> result = await CreateSut()
            .Handle(new GetAppointmentsQuery(From: null, To: cutoff), default);

        result.Should().ContainSingle(a => a.Date == early);
        result.Should().NotContain(a => a.Date == late);
    }

    [Fact]
    public async Task Handle_BothFilters_ReturnsOnlyAppointmentsInRange()
    {
        DateTime tooEarly = DateTime.UtcNow.AddDays(1);
        DateTime inRange  = DateTime.UtcNow.AddDays(5);
        DateTime tooLate  = DateTime.UtcNow.AddDays(10);
        await SeedAppointment(tooEarly);
        await SeedAppointment(inRange);
        await SeedAppointment(tooLate);

        List<AppointmentResponse> result = await CreateSut()
            .Handle(new GetAppointmentsQuery(
                From: DateTime.UtcNow.AddDays(3),
                To:   DateTime.UtcNow.AddDays(7)), default);

        result.Should().ContainSingle(a => a.Date == inRange);
        result.Should().NotContain(a => a.Date == tooEarly);
        result.Should().NotContain(a => a.Date == tooLate);
    }

    [Fact]
    public async Task Handle_EmptyDb_ReturnsEmptyList()
    {
        List<AppointmentResponse> result = await CreateSut()
            .Handle(new GetAppointmentsQuery(null, null), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ArtistCaller_ReturnsOnlyOwnAppointments()
    {
        FakeCurrentUser artistUser = FakeCurrentUser.Artist();
        Guid myArtistId = await SeedArtistForUser(artistUser.UserId);
        Guid otherArtistId = Guid.NewGuid();
        await SeedAppointment(DateTime.UtcNow.AddDays(1), myArtistId);
        await SeedAppointment(DateTime.UtcNow.AddDays(2), otherArtistId);

        GetAppointmentsHandler sut = new(_db, artistUser);
        List<AppointmentResponse> result = await sut.Handle(new GetAppointmentsQuery(null, null), default);

        result.Should().ContainSingle(a => a.ArtistId == myArtistId);
        result.Should().NotContain(a => a.ArtistId == otherArtistId);
    }

    [Fact]
    public async Task Handle_ArtistCaller_IgnoresRequestedArtistIdFilter()
    {
        FakeCurrentUser artistUser = FakeCurrentUser.Artist();
        Guid myArtistId = await SeedArtistForUser(artistUser.UserId);
        Guid otherArtistId = Guid.NewGuid();
        await SeedAppointment(DateTime.UtcNow.AddDays(1), myArtistId);
        await SeedAppointment(DateTime.UtcNow.AddDays(2), otherArtistId);

        GetAppointmentsHandler sut = new(_db, artistUser);
        List<AppointmentResponse> result = await sut.Handle(
            new GetAppointmentsQuery(null, null, otherArtistId), default);

        result.Should().ContainSingle(a => a.ArtistId == myArtistId);
    }

    [Fact]
    public async Task Handle_OwnerCaller_ArtistIdFilter_ReturnsOnlyThatArtist()
    {
        Guid targetArtistId = Guid.NewGuid();
        Guid otherArtistId  = Guid.NewGuid();
        await SeedAppointment(DateTime.UtcNow.AddDays(1), targetArtistId);
        await SeedAppointment(DateTime.UtcNow.AddDays(2), otherArtistId);

        List<AppointmentResponse> result = await CreateSut()
            .Handle(new GetAppointmentsQuery(null, null, targetArtistId), default);

        result.Should().ContainSingle(a => a.ArtistId == targetArtistId);
    }

    private async Task<Guid> SeedArtistForUser(Guid userId)
    {
        var artist = new Artist
        {
            StudioId  = _studioId,
            UserId    = userId,
            FirstName = "Art",
            LastName  = "Ist",
            Email     = $"{Guid.NewGuid()}@test.com",
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist.Id;
    }

    private async Task SeedAppointment(DateTime date, Guid? artistId = null)
    {
        Client client = new() { StudioId = _studioId, FirstName = "Test", LastName = "Client", Email = $"{Guid.NewGuid()}@test.com" };
        _db.Clients.Add(client);

        _db.Appointments.Add(new Appointment
        {
            StudioId        = _studioId,
            ArtistId        = artistId ?? Guid.NewGuid(),
            ClientId        = client.Id,
            Date            = date,
            EndDate         = date.AddHours(1),
            DurationMinutes = 60,
            Status          = AppointmentStatus.Pending,
            DepositStatus   = DepositStatus.Pending
        });
        await _db.SaveChangesAsync();
    }
}
