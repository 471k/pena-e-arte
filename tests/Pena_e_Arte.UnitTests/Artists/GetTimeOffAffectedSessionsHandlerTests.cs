using FluentAssertions;
using FluentValidation.Results;
using Pena_e_Arte.Application.Artists.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

/// <summary>
/// E04c (A06): which booked sessions a time-off range covers. Time off is studio-local whole days
/// (inclusive); appointments are UTC instants. The studio here is Europe/Tirane, UTC+1 in November, so
/// the range 10–12 Nov 2026 is the UTC window [9 Nov 23:00, 12 Nov 23:00).
/// </summary>
public class GetTimeOffAffectedSessionsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _artistUserId = Guid.NewGuid();
    private readonly Guid _artistId;
    private readonly Guid _otherArtistId;
    private readonly Guid _clientId;

    private static readonly DateTime From = new(2026, 11, 10);
    private static readonly DateTime To = new(2026, 11, 12);
    private static readonly DateTime WindowStart = new(2026, 11, 9, 23, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WindowEnd = new(2026, 11, 12, 23, 0, 0, DateTimeKind.Utc);

    public GetTimeOffAffectedSessionsHandlerTests()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "Ink", Slug = _studioId.ToString("N")[..8], Timezone = "Europe/Tirane" });
        Artist artist = new() { StudioId = _studioId, UserId = _artistUserId, FirstName = "Ari", LastName = "Kola", Email = "a@a.test", IsActive = true };
        Artist other = new() { StudioId = _studioId, UserId = Guid.NewGuid(), FirstName = "Oth", LastName = "Er", Email = "o@a.test", IsActive = true };
        Client client = new() { StudioId = _studioId, FirstName = "Mira", LastName = "Hoxha", Email = "m@c.test" };
        _db.Artists.AddRange(artist, other);
        _db.Clients.Add(client);
        _db.SaveChanges();
        _artistId = artist.Id;
        _otherArtistId = other.Id;
        _clientId = client.Id;
    }

    private Guid Seed(DateTime startUtc, DateTime endUtc, AppointmentStatus status = AppointmentStatus.Confirmed, Guid? artistId = null)
    {
        Appointment a = new()
        {
            StudioId = _studioId,
            ArtistId = artistId ?? _artistId,
            ClientId = _clientId,
            Date = startUtc,
            EndDate = endUtc,
            DurationMinutes = (int)(endUtc - startUtc).TotalMinutes,
            Status = status,
        };
        _db.Appointments.Add(a);
        _db.SaveChanges();
        return a.Id;
    }

    private Task<TimeOffAffectedSessionsResponse> Read(FakeCurrentUser? user = null, DateTime? from = null, DateTime? to = null) =>
        new GetTimeOffAffectedSessionsHandler(_db, user ?? FakeCurrentUser.Owner())
            .Handle(new GetTimeOffAffectedSessionsQuery(_artistId, from ?? From, to ?? To), default);

    private static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public void TheWindow_IsLocalMidnightToTheMidnightAfterTheLastDay_InTheStudioTimezone()
    {
        (DateTime start, DateTime end) = GetTimeOffAffectedSessionsHandler.UtcWindow(From, To, "Europe/Tirane");

        start.Should().Be(WindowStart);
        end.Should().Be(WindowEnd);
    }

    [Fact]
    public void WithNoTimezone_TheWindowIsInUtc()
    {
        (DateTime start, DateTime end) = GetTimeOffAffectedSessionsHandler.UtcWindow(From, To, null);

        start.Should().Be(new DateTime(2026, 11, 10, 0, 0, 0, DateTimeKind.Utc));
        end.Should().Be(new DateTime(2026, 11, 13, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task NoSessions_IsAnEmptyList()
    {
        TimeOffAffectedSessionsResponse r = await Read();

        r.Sessions.Should().BeEmpty();
        r.StartDate.Should().Be(From);
        r.EndDate.Should().Be(To);
        r.IsProjection.Should().BeTrue();
    }

    [Fact]
    public async Task OneSessionInside_IsListed_WithClientAndStatus()
    {
        Guid id = Seed(Utc(11, 10), Utc(11, 12));

        TimeOffAffectedSessionResponse s = (await Read()).Sessions.Should().ContainSingle().Subject;

        s.AppointmentId.Should().Be(id);
        s.ClientName.Should().Be("Mira Hoxha");
        s.Status.Should().Be("Confirmed");
        s.Date.Should().Be(Utc(11, 10));
        s.EndDate.Should().Be(Utc(11, 12));
    }

    [Fact]
    public async Task SeveralSessions_AreListedEarliestFirst()
    {
        Guid late = Seed(Utc(12, 9), Utc(12, 10));
        Guid early = Seed(Utc(10, 9), Utc(10, 10), AppointmentStatus.Pending);
        Guid middle = Seed(Utc(11, 9), Utc(11, 10));

        (await Read()).Sessions.Select(s => s.AppointmentId).Should().Equal(early, middle, late);
    }

    [Fact]
    public async Task ASessionEndingExactlyAtTheFirstLocalMidnight_IsNotAffected()
    {
        Seed(Utc(9, 22), WindowStart);

        (await Read()).Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task ASessionStartingExactlyAtTheMidnightAfterTheLastDay_IsNotAffected()
    {
        Seed(WindowEnd, WindowEnd.AddHours(2));

        (await Read()).Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task ASessionStartingBeforeTheRange_AndEndingInsideIt_IsAffected()
    {
        // 22:30–01:30 local, the night before the first day into the first day.
        Guid id = Seed(Utc(9, 21, 30), Utc(10, 0, 30));

        (await Read()).Sessions.Select(s => s.AppointmentId).Should().Equal(id);
    }

    [Fact]
    public async Task ASessionStartingInsideTheRange_AndEndingAfterIt_IsAffected()
    {
        // 23:00–02:00 local, the last day into the day after.
        Guid id = Seed(Utc(12, 22), Utc(13, 1));

        (await Read()).Sessions.Select(s => s.AppointmentId).Should().Equal(id);
    }

    [Fact]
    public async Task ASessionSpanningTheWholeRange_IsAffected()
    {
        Guid id = Seed(Utc(9, 12), Utc(13, 12));

        (await Read()).Sessions.Select(s => s.AppointmentId).Should().Equal(id);
    }

    [Fact]
    public async Task TheStudiosLocalDay_Decides_NotTheUtcDate()
    {
        // 9 Nov 23:30 UTC is 00:30 on 10 Nov in Tirane: inside. 12 Nov 23:30 UTC is 00:30 on 13 Nov: outside.
        Guid inside = Seed(Utc(9, 23, 30), Utc(10, 0, 30));
        Seed(Utc(12, 23, 30), Utc(13, 0, 30));

        (await Read()).Sessions.Select(s => s.AppointmentId).Should().Equal(inside);
    }

    [Fact]
    public async Task ASingleDayRange_CoversThatWholeLocalDay()
    {
        Guid morning = Seed(Utc(9, 23), Utc(10, 0));     // 00:00–01:00 local on the 10th
        Guid night = Seed(Utc(10, 22), Utc(10, 22, 59)); // 23:00–23:59 local on the 10th
        Seed(Utc(10, 23), Utc(11, 0));                    // 00:00 local on the 11th

        (await Read(from: From, to: From)).Sessions.Select(s => s.AppointmentId).Should().Equal(morning, night);
    }

    [Theory]
    [InlineData(AppointmentStatus.Cancelled)]
    [InlineData(AppointmentStatus.Completed)]
    [InlineData(AppointmentStatus.NoShow)]
    public async Task SessionsNoLongerBooked_AreNotAffected(AppointmentStatus status)
    {
        Seed(Utc(11, 10), Utc(11, 12), status);

        (await Read()).Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task AnotherArtistsSessions_AreNotListed()
    {
        Seed(Utc(11, 10), Utc(11, 12), artistId: _otherArtistId);

        (await Read()).Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task AnArtist_MayAskAboutTheirOwnTimeOff()
    {
        Seed(Utc(11, 10), Utc(11, 12));

        (await Read(new FakeCurrentUser(_artistUserId, "artist"))).Sessions.Should().ContainSingle();
    }

    [Fact]
    public async Task AnArtist_MayNotAskAboutAnotherArtist_ExactlyAsAddingTimeOffRefuses()
    {
        Func<Task> act = () => Read(FakeCurrentUser.Artist());

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task AnUnknownArtist_IsNotFound()
    {
        Func<Task> act = () => new GetTimeOffAffectedSessionsHandler(_db, FakeCurrentUser.Owner())
            .Handle(new GetTimeOffAffectedSessionsQuery(Guid.NewGuid(), From, To), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void Validator_RefusesAnEndBeforeTheStart_AndAnOverlongRange()
    {
        GetTimeOffAffectedSessionsValidator v = new();

        v.Validate(new GetTimeOffAffectedSessionsQuery(_artistId, From, To)).IsValid.Should().BeTrue();
        v.Validate(new GetTimeOffAffectedSessionsQuery(_artistId, To, From)).IsValid.Should().BeFalse();
        ValidationResult tooLong = v.Validate(new GetTimeOffAffectedSessionsQuery(_artistId, From, From.AddDays(367)));
        tooLong.IsValid.Should().BeFalse();
        v.Validate(new GetTimeOffAffectedSessionsQuery(Guid.Empty, From, To)).IsValid.Should().BeFalse();
    }
}
