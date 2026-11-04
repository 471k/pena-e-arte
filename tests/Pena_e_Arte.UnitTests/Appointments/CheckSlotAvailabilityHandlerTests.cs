using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class CheckSlotAvailabilityHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly ICurrentUser _staff = Substitute.For<ICurrentUser>();

    public CheckSlotAvailabilityHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _staff.Role.Returns("artist");
    }

    private CheckSlotAvailabilityHandler CreateSut() => new(_db, _tenant, _staff);

    private void SeedSchedule(DayOfWeek day)
    {
        _db.ArtistSchedules.Add(new ArtistSchedule
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            DayOfWeek = day,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(18),
            IsAvailable = true,
        });
        _db.SaveChanges();
    }

    // Studio-wide hours gate (added 2026-09-09) — separate helper since not every test
    // that seeds an ArtistSchedule also wants studio hours open (e.g. none needed for the
    // "no artist available" case).
    private void SeedStudioHours(DayOfWeek day)
    {
        _db.StudioHours.Add(new StudioHours
        {
            StudioId = _studioId,
            DayOfWeek = day,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(18),
            IsOpen = true,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Handle_NoClosureAndWithinSchedule_ReturnsAvailable()
    {
        DateTime slot = NextDateForDay(DayOfWeek.Monday);
        SeedSchedule(slot.DayOfWeek);
        SeedStudioHours(slot.DayOfWeek);

        SlotAvailabilityResult result = await CreateSut().Handle(
            new CheckSlotAvailabilityQuery(_artistId, slot.AddHours(10), 60), default);

        result.Available.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_StudioClosedThatDay_ReturnsUnavailable()
    {
        DateTime slot = NextDateForDay(DayOfWeek.Monday);
        SeedSchedule(slot.DayOfWeek);

        _db.StudioClosures.Add(new StudioClosure
        {
            StudioId = _studioId,
            StartDate = slot.Date,
            EndDate = slot.Date,
            Reason = "Public holiday",
        });
        _db.SaveChanges();

        SlotAvailabilityResult result = await CreateSut().Handle(
            new CheckSlotAvailabilityQuery(_artistId, slot.AddHours(10), 60), default);

        result.Available.Should().BeFalse();
        result.Reason.Should().Be("Studio is closed that day.");
    }

    [Fact]
    public async Task Handle_ClosureOnDifferentDay_DoesNotAffectAvailability()
    {
        DateTime slot = NextDateForDay(DayOfWeek.Monday);
        SeedSchedule(slot.DayOfWeek);
        SeedStudioHours(slot.DayOfWeek);

        _db.StudioClosures.Add(new StudioClosure
        {
            StudioId = _studioId,
            StartDate = slot.Date.AddDays(10),
            EndDate = slot.Date.AddDays(12),
            Reason = "Renovation",
        });
        _db.SaveChanges();

        SlotAvailabilityResult result = await CreateSut().Handle(
            new CheckSlotAvailabilityQuery(_artistId, slot.AddHours(10), 60), default);

        result.Available.Should().BeTrue();
    }

    private Guid SeedActiveArtist()
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            FirstName = "Any",
            LastName = "Artist",
            Email = $"{Guid.NewGuid()}@artist.test",
        };
        _db.Artists.Add(artist);
        _db.SaveChanges();
        return artist.Id;
    }

    [Fact]
    public async Task Handle_NullArtistId_AnActiveArtistIsAvailable_ReturnsAvailable()
    {
        DateTime slot = NextDateForDay(DayOfWeek.Monday);
        Guid artistId = SeedActiveArtist();
        _db.ArtistSchedules.Add(new ArtistSchedule
        {
            StudioId = _studioId,
            ArtistId = artistId,
            DayOfWeek = slot.DayOfWeek,
            StartTime = TimeSpan.FromHours(9),
            EndTime = TimeSpan.FromHours(18),
            IsAvailable = true,
        });
        _db.SaveChanges();
        SeedStudioHours(slot.DayOfWeek);

        SlotAvailabilityResult result = await CreateSut().Handle(
            new CheckSlotAvailabilityQuery(null, slot.AddHours(10), 60), default);

        result.Available.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NullArtistId_NoActiveArtist_ReturnsUnavailable()
    {
        DateTime slot = NextDateForDay(DayOfWeek.Monday);

        SlotAvailabilityResult result = await CreateSut().Handle(
            new CheckSlotAvailabilityQuery(null, slot.AddHours(10), 60), default);

        result.Available.Should().BeFalse();
        result.Reason.Should().Be("No artist is available at that time.");
    }

    // ── E04b (A02): excluding the appointment being moved ──────────────────────

    [Fact]
    public async Task ExcludingTheAppointmentBeingMoved_LetsItMoveWithinItsOwnSlot()
    {
        DateTime day = NextDateForDay(DayOfWeek.Monday);
        SeedSchedule(day.DayOfWeek);
        SeedStudioHours(day.DayOfWeek);
        Guid moving = SeedAppointment(day.AddHours(10), 120);

        // An hour later, overlapping its own current 10:00–12:00 slot.
        SlotAvailabilityResult without = await CreateSut().Handle(
            new CheckSlotAvailabilityQuery(_artistId, day.AddHours(11), 120), default);
        SlotAvailabilityResult with = await CreateSut().Handle(
            new CheckSlotAvailabilityQuery(_artistId, day.AddHours(11), 120, moving), default);

        without.Available.Should().BeFalse("without the exclusion the appointment blocks its own move");
        with.Available.Should().BeTrue();
    }

    [Fact]
    public async Task ExcludingTheAppointmentBeingMoved_StillRejectsARealOverlap()
    {
        DateTime day = NextDateForDay(DayOfWeek.Monday);
        SeedSchedule(day.DayOfWeek);
        SeedStudioHours(day.DayOfWeek);
        Guid moving = SeedAppointment(day.AddHours(10), 60);
        SeedAppointment(day.AddHours(14), 60);

        SlotAvailabilityResult result = await CreateSut().Handle(
            new CheckSlotAvailabilityQuery(_artistId, day.AddHours(14), 60, moving), default);

        result.Available.Should().BeFalse();
        result.Reason.Should().Be("That slot is already booked.");
    }

    [Fact]
    public async Task AClient_CannotExcludeSomeoneElsesAppointment()
    {
        DateTime day = NextDateForDay(DayOfWeek.Monday);
        Guid someoneElses = SeedAppointment(day.AddHours(10), 60);
        ICurrentUser client = Substitute.For<ICurrentUser>();
        client.Role.Returns("client");
        client.UserId.Returns(Guid.NewGuid());
        _db.Clients.Add(new Client { StudioId = _studioId, UserId = client.UserId, FirstName = "Me", LastName = "Client", Email = $"{Guid.NewGuid():N}@c.test" });
        _db.SaveChanges();

        Func<Task> act = () => new CheckSlotAvailabilityHandler(_db, _tenant, client).Handle(
            new CheckSlotAvailabilityQuery(_artistId, day.AddHours(11), 60, someoneElses), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task AClient_CanExcludeTheirOwnAppointment()
    {
        DateTime day = NextDateForDay(DayOfWeek.Monday);
        SeedSchedule(day.DayOfWeek);
        SeedStudioHours(day.DayOfWeek);
        ICurrentUser client = Substitute.For<ICurrentUser>();
        client.Role.Returns("client");
        client.UserId.Returns(Guid.NewGuid());
        Guid mine = SeedAppointment(day.AddHours(10), 120, clientUserId: client.UserId);

        SlotAvailabilityResult result = await new CheckSlotAvailabilityHandler(_db, _tenant, client).Handle(
            new CheckSlotAvailabilityQuery(_artistId, day.AddHours(11), 120, mine), default);

        result.Available.Should().BeTrue();
    }

    [Fact]
    public async Task ExcludingAnAppointmentThatDoesNotExist_IsNotFound()
    {
        DateTime day = NextDateForDay(DayOfWeek.Monday);

        Func<Task> act = () => CreateSut().Handle(
            new CheckSlotAvailabilityQuery(_artistId, day.AddHours(11), 60, Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private Guid SeedAppointment(DateTime start, int minutes, Guid? clientUserId = null)
    {
        Client owner = new() { StudioId = _studioId, UserId = clientUserId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid():N}@c.test" };
        _db.Clients.Add(owner);
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            ClientId = owner.Id,
            Date = start,
            EndDate = start.AddMinutes(minutes),
            DurationMinutes = minutes,
            Status = Domain.Enums.AppointmentStatus.Confirmed,
        };
        _db.Appointments.Add(appointment);
        _db.SaveChanges();
        return appointment.Id;
    }

    private static DateTime NextDateForDay(DayOfWeek day)
    {
        DateTime date = DateTime.UtcNow.Date.AddDays(1);
        while (date.DayOfWeek != day) date = date.AddDays(1);
        return date;
    }
}
