using FluentAssertions;
using FluentValidation.Results;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Application.Appointments.Validators;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Application.Public.Validators;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

/// <summary>C03 (slice 7): the two slot-list handlers and their validators. The rule itself is in AvailableSlotsTests.</summary>
public class GetAvailableSlotsHandlerTests
{
    // Monday 7 June 2027, 10:10 UTC, in a UTC studio open 08:00-18:00 with 30-minute starts.
    private static readonly DateTimeOffset Now = new(2027, 6, 7, 10, 10, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2027, 6, 7);

    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();
    private readonly FixedTimeProvider _clock = new(Now);
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _artistId = Guid.NewGuid();

    public GetAvailableSlotsHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _user.Role.Returns("owner");
        _db.Studios.Add(new Studio
        {
            Id = _studioId,
            Name = "S",
            Slug = "slot-studio",
            OwnerEmail = "o@x.test",
            Timezone = "UTC",
            IsPublished = true,
            IsActive = true,
        });
        _db.Artists.Add(new Artist { Id = _artistId, StudioId = _studioId, FirstName = "A", LastName = "B", Email = "a@b.test" });
        _db.StudioHours.Add(new StudioHours { StudioId = _studioId, DayOfWeek = Today.DayOfWeek, IsOpen = true, StartTime = TimeSpan.FromHours(8), EndTime = TimeSpan.FromHours(18) });
        _db.ArtistSchedules.Add(new ArtistSchedule { StudioId = _studioId, ArtistId = _artistId, DayOfWeek = Today.DayOfWeek, IsAvailable = true, StartTime = TimeSpan.FromHours(8), EndTime = TimeSpan.FromHours(18) });
        _db.SaveChanges();
    }

    private GetAvailableSlotsHandler Handler() => new(_db, _tenant, _user, _clock);

    private static DateTime TodayAt(int hour, int minute) => new(2027, 6, 7, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public async Task A_booking_is_offered_nothing_inside_the_30_minute_lead_CreateAppointmentValidator_enforces()
    {
        AvailableSlotsResult result = await Handler().Handle(new GetAvailableSlotsQuery(_artistId, Today, Today, 30), default);

        // Now 10:10, so the lead ends 10:40: 10:30 is refused by the booking validator and must not be offered.
        result.Days.Single().Starts.First().Should().Be(TodayAt(11, 0));
        result.NextAvailable.Should().Be(TodayAt(11, 0));
    }

    [Fact]
    public async Task A_reschedule_is_offered_any_future_start_as_RescheduleAppointmentValidator_allows()
    {
        Guid moving = SeedAppointment(Guid.NewGuid(), TodayAt(15, 0));

        AvailableSlotsResult result = await Handler().Handle(
            new GetAvailableSlotsQuery(_artistId, Today, Today, 30, moving), default);

        result.Days.Single().Starts.First().Should().Be(TodayAt(10, 30));
        result.Days.Single().Starts.Should().Contain(TodayAt(15, 0), "the appointment being moved doesn't block itself");
    }

    [Fact]
    public async Task A_client_cannot_name_another_clients_appointment_as_the_one_being_moved()
    {
        Guid someoneElses = SeedAppointment(Guid.NewGuid(), TodayAt(15, 0));
        Guid myClientId = Guid.NewGuid();
        Guid myUserId = Guid.NewGuid();
        _db.Clients.Add(new Client { Id = myClientId, StudioId = _studioId, UserId = myUserId, FirstName = "C", LastName = "D", Email = "c@d.test" });
        _db.SaveChanges();
        _user.Role.Returns("client");
        _user.UserId.Returns(myUserId);

        Func<Task> act = () => Handler().Handle(new GetAvailableSlotsQuery(_artistId, Today, Today, 30, someoneElses), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task The_public_list_reads_the_published_studio_by_slug_and_keeps_the_booking_lead()
    {
        AvailableSlotsResult result = await new GetPublicAvailableSlotsHandler(_db, _clock)
            .Handle(new GetPublicAvailableSlotsQuery("slot-studio", _artistId, Today, Today, 30), default);

        result.Days.Single().Starts.First().Should().Be(TodayAt(11, 0));
    }

    [Fact]
    public async Task The_public_list_for_an_unknown_studio_is_not_found()
    {
        Func<Task> act = () => new GetPublicAvailableSlotsHandler(_db, _clock)
            .Handle(new GetPublicAvailableSlotsQuery("no-such-studio", null, Today, Today, 30), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData(0, 13, 60, true)]    // 14 days, the cap
    [InlineData(0, 14, 60, false)]   // 15 days
    [InlineData(3, 2, 60, false)]    // To before From
    [InlineData(-2, 0, 60, false)]   // From in the past
    [InlineData(0, 0, 20, false)]    // duration under 30
    [InlineData(0, 0, 481, false)]   // duration over 480
    public void Both_validators_cap_the_range_and_the_duration(int fromOffset, int toOffset, int duration, bool valid)
    {
        DateOnly from = Today.AddDays(fromOffset);
        DateOnly to = Today.AddDays(toOffset);

        ValidationResult signedIn = new GetAvailableSlotsValidator(_clock).Validate(new GetAvailableSlotsQuery(null, from, to, duration));
        ValidationResult anonymous = new GetPublicAvailableSlotsValidator(_clock).Validate(new GetPublicAvailableSlotsQuery("s", null, from, to, duration));

        signedIn.IsValid.Should().Be(valid);
        anonymous.IsValid.Should().Be(valid);
    }

    private Guid SeedAppointment(Guid clientId, DateTime start)
    {
        Appointment a = new()
        {
            StudioId = _studioId,
            ArtistId = _artistId,
            ClientId = clientId,
            Date = start,
            EndDate = start.AddMinutes(30),
            DurationMinutes = 30,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Pending,
        };
        _db.Appointments.Add(a);
        _db.SaveChanges();
        return a.Id;
    }
}
