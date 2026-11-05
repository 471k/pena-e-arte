using FluentAssertions;
using MediatR;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

/// <summary>
/// Phi, 2026-11-05: the reschedule save now checks the artist is actually working at the new time (studio
/// closures and hours, the artist's own hours and time off), for staff and clients alike, the same check
/// booking makes and the reschedule preview already showed. Before, the save checked only overlaps for an
/// assigned artist.
/// </summary>
public class RescheduleScheduleCheckTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();
    private readonly Guid _clientUserId = Guid.NewGuid();
    private readonly DateTime _monday;

    public RescheduleScheduleCheckTests()
    {
        _tenant.StudioId.Returns(_studioId);
        DateTime d = DateTime.UtcNow.Date.AddDays(7);
        while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);
        _monday = d;
        // Open Monday 09:00–18:00, artist working the same hours.
        _db.StudioHours.Add(new StudioHours { StudioId = _studioId, DayOfWeek = DayOfWeek.Monday, StartTime = TimeSpan.FromHours(9), EndTime = TimeSpan.FromHours(18), IsOpen = true });
        _db.ArtistSchedules.Add(new ArtistSchedule { StudioId = _studioId, ArtistId = _artistId, DayOfWeek = DayOfWeek.Monday, StartTime = TimeSpan.FromHours(9), EndTime = TimeSpan.FromHours(18), IsAvailable = true });
        _db.SaveChanges();
    }

    private Task Reschedule(string role, Guid id, DateTime newStart)
    {
        ICurrentUser user = Substitute.For<ICurrentUser>();
        user.Role.Returns(role);
        user.UserId.Returns(role == "client" ? _clientUserId : Guid.NewGuid());
        return new RescheduleAppointmentHandler(_db, _tenant, user, Substitute.For<IRealtimeNotifier>(), Substitute.For<ISender>(), Substitute.For<IJobScheduler>())
            .Handle(new RescheduleAppointmentCommand(id, new RescheduleAppointmentRequest(newStart, 60, null)), default);
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("artist")]
    [InlineData("client")]
    public async Task OutsideStudioHours_IsRefused_ForEveryone_AndNothingIsSaved(string role)
    {
        Guid id = SeedAppointment(_monday.AddHours(10));

        Func<Task> act = () => Reschedule(role, id, _monday.AddHours(20));

        (await act.Should().ThrowAsync<BusinessRuleViolationException>()).Which.Message.Should().StartWith("Outside studio hours");
        _db.ChangeTracker.Clear();
        _db.Appointments.Single(a => a.Id == id).Date.Should().Be(_monday.AddHours(10));
    }

    [Fact]
    public async Task OnTheArtistsDayOff_IsRefused()
    {
        Guid id = SeedAppointment(_monday.AddHours(10));
        _db.ArtistTimeOffs.Add(new ArtistTimeOff { StudioId = _studioId, ArtistId = _artistId, StartDate = _monday, EndDate = _monday });
        _db.SaveChanges();

        Func<Task> act = () => Reschedule("owner", id, _monday.AddHours(14));

        (await act.Should().ThrowAsync<BusinessRuleViolationException>()).Which.Message.Should().Be("Artist is on leave that day.");
    }

    [Fact]
    public async Task OnADayTheStudioIsClosed_IsRefused()
    {
        Guid id = SeedAppointment(_monday.AddHours(10));
        _db.StudioClosures.Add(new StudioClosure { StudioId = _studioId, StartDate = _monday, EndDate = _monday, Reason = "Holiday" });
        _db.SaveChanges();

        Func<Task> act = () => Reschedule("owner", id, _monday.AddHours(14));

        (await act.Should().ThrowAsync<BusinessRuleViolationException>()).Which.Message.Should().Be("Studio is closed that day.");
    }

    [Fact]
    public async Task WithinHours_StillWorks()
    {
        Guid id = SeedAppointment(_monday.AddHours(10));

        await Reschedule("client", id, _monday.AddHours(15));

        _db.ChangeTracker.Clear();
        _db.Appointments.Single(a => a.Id == id).Date.Should().Be(_monday.AddHours(15));
    }

    [Fact]
    public async Task ThePreviewAndTheSave_GiveTheSameReason()
    {
        Guid id = SeedAppointment(_monday.AddHours(10));
        ICurrentUser staff = Substitute.For<ICurrentUser>();
        staff.Role.Returns("owner");

        SlotAvailabilityResult preview = await new CheckSlotAvailabilityHandler(_db, _tenant, staff)
            .Handle(new CheckSlotAvailabilityQuery(_artistId, _monday.AddHours(20), 60, id), default);
        Func<Task> save = () => Reschedule("owner", id, _monday.AddHours(20));

        preview.Available.Should().BeFalse();
        (await save.Should().ThrowAsync<BusinessRuleViolationException>()).Which.Message.Should().Be(preview.Reason);
    }

    private Guid SeedAppointment(DateTime start)
    {
        Client client = new() { StudioId = _studioId, UserId = _clientUserId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid():N}@c.test" };
        _db.Clients.Add(client);
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            ClientId = client.Id,
            Date = start,
            EndDate = start.AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Pending,
        };
        _db.Appointments.Add(appointment);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        return appointment.Id;
    }
}
