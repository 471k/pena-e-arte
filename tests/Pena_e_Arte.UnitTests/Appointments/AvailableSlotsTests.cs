using FluentAssertions;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

/// <summary>
/// C03 (slice 7): the slot list is a preview, and it must never offer a start the authority would refuse. The
/// property test builds random studios and compares every offered start with the authority itself:
/// CheckArtistSlotAvailabilityAsync for a specific artist, IsAnyArtistAvailableAsync for any artist. If the list and
/// the authority ever disagree, the list is lying to clients.
/// </summary>
public class AvailableSlotsTests
{
    private static readonly DateTime LongAgo = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    // ── The property ───────────────────────────────────────────────────────────────────────────

    public static TheoryData<int, string, DateOnly, bool> Scenarios()
    {
        string[] zones = ["Europe/Paris", "America/New_York", "Pacific/Auckland", "UTC"];
        // Ranges that cross each zone's DST changes in 2027, plus an ordinary week.
        DateOnly[] starts =
        [
            new(2027, 3, 24),  // EU spring forward 28 Mar
            new(2027, 10, 27), // EU fall back 31 Oct
            new(2027, 3, 11),  // US spring forward 14 Mar
            new(2027, 11, 4),  // US fall back 7 Nov
            new(2027, 4, 1),   // NZ fall back 4 Apr
            new(2027, 9, 23),  // NZ spring forward 26 Sep
            new(2027, 6, 7),
        ];
        TheoryData<int, string, DateOnly, bool> data = new();
        for (int seed = 1; seed <= 42; seed++)
            data.Add(seed, zones[seed % zones.Length], starts[seed % starts.Length], seed % 2 == 0);
        return data;
    }

    [Theory]
    [MemberData(nameof(Scenarios))]
    public async Task EveryOfferedStartIsOneTheAuthorityAccepts_AndNoneOnItsOwnGridIsMissed(
        int seed, string zone, DateOnly from, bool anyArtist)
    {
        Random rng = new(seed);
        FakeDbContext db = FakeDbContext.Create();
        RandomStudio studio = RandomStudio.Seed(db, rng, zone, from);
        int duration = new[] { 30, 45, 60, 90, 120, 180, 240, 480 }[rng.Next(8)];
        DateOnly to = from.AddDays(6);
        Guid? artistId = anyArtist ? null : studio.ArtistIds[rng.Next(studio.ArtistIds.Count)];
        Guid? exclude = !anyArtist && studio.AppointmentIdsByArtist.TryGetValue(artistId!.Value, out List<Guid>? own)
                        && own.Count > 0 && rng.Next(2) == 0
            ? own[rng.Next(own.Count)]
            : null;
        // Earliest start in the middle of the range, so the cut-off is exercised too.
        DateTime earliest = TimezoneUtils.StudioLocalToUtcForRules(from.AddDays(1).ToDateTime(new TimeOnly(13, 0)), zone);

        AvailableSlotsResult result = await db.FindAvailableSlotsAsync(
            studio.StudioId, artistId, from, to, duration, earliest, exclude, default);

        result.Days.Select(d => d.Date).Should().Equal(Enumerable.Range(0, 7).Select(i => from.AddDays(i)));

        async Task<bool> Authority(DateTime utc) => artistId is Guid a
            ? (await db.CheckArtistSlotAvailabilityAsync(studio.StudioId, a, utc, duration, default, exclude)).Available
            : await db.IsAnyArtistAvailableAsync(studio.StudioId, utc, duration, default);

        foreach (AvailableSlotsDay day in result.Days)
        {
            day.Starts.Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
            foreach (DateTime start in day.Starts)
            {
                start.Should().BeAfter(earliest);
                DateOnly.FromDateTime(TimezoneUtils.ToStudioLocalForRules(start, zone)).Should().Be(day.Date);
                (await Authority(start)).Should().BeTrue(
                    $"seed {seed}: {start:O} was offered, so the authority must accept it");
            }

            // Completeness on the list's own grid: duration steps from the later of the studio's and an artist's
            // opening time. A start the authority accepts there must be offered.
            foreach (DateTime candidate in studio.GridFor(day.Date, duration, artistId))
            {
                if (candidate <= earliest) continue;
                if (DateOnly.FromDateTime(TimezoneUtils.ToStudioLocalForRules(candidate, zone)) != day.Date) continue;
                if (await Authority(candidate))
                    day.Starts.Should().Contain(candidate, $"seed {seed}: the authority accepts {candidate:O}");
            }
        }

        DateTime? firstInRange = result.Days.SelectMany(d => d.Starts).Cast<DateTime?>().FirstOrDefault();
        if (firstInRange is not null)
            result.NextAvailable.Should().Be(firstInRange);
        else if (result.NextAvailable is DateTime next)
        {
            next.Should().BeAfter(earliest);
            (await Authority(next)).Should().BeTrue();
        }
    }

    // ── Each rule excludes what it should ──────────────────────────────────────────────────────

    private static readonly DateOnly Monday = new(2027, 6, 7);

    [Fact]
    public async Task A_duration_that_would_overrun_closing_time_is_not_offered()
    {
        Fixture f = Fixture.Create("UTC");
        f.Open(Monday.DayOfWeek, 10, 18);
        Guid artist = f.Artist(Monday.DayOfWeek, 10, 18);

        AvailableSlotsDay day = (await f.Find(artist, Monday, Monday, 180)).Days.Single();

        day.Starts.Should().Equal(At(Monday, 10, "UTC"), At(Monday, 13, "UTC")); // 16:00 would end at 19:00
    }

    [Fact]
    public async Task A_closure_empties_the_day_and_time_off_removes_the_artist()
    {
        Fixture f = Fixture.Create("UTC");
        f.Open(Monday.DayOfWeek, 10, 18);
        f.Open(Monday.AddDays(1).DayOfWeek, 10, 18);
        Guid artist = f.Artist(Monday.DayOfWeek, 10, 18);
        f.Schedule(artist, Monday.AddDays(1).DayOfWeek, 10, 18);
        f.Db.StudioClosures.Add(new StudioClosure { StudioId = f.StudioId, StartDate = Day(Monday), EndDate = Day(Monday), Reason = "Holiday" });
        f.Db.ArtistTimeOffs.Add(new ArtistTimeOff { StudioId = f.StudioId, ArtistId = artist, StartDate = Day(Monday.AddDays(1)), EndDate = Day(Monday.AddDays(1)) });
        await f.Db.SaveChangesAsync();

        AvailableSlotsResult result = await f.Find(artist, Monday, Monday.AddDays(1), 60);

        result.Days.Should().AllSatisfy(d => d.Starts.Should().BeEmpty());
        result.NextAvailable.Should().NotBeNull("the next Monday is open");
        DateOnly.FromDateTime(result.NextAvailable!.Value).Should().Be(Monday.AddDays(7));
    }

    [Fact]
    public async Task Booked_time_is_not_offered_but_cancelled_deleted_and_other_studios_bookings_free_it()
    {
        Fixture f = Fixture.Create("UTC");
        f.Open(Monday.DayOfWeek, 10, 14);
        Guid artist = f.Artist(Monday.DayOfWeek, 10, 14);
        f.Booking(artist, At(Monday, 10, "UTC"), 60);                                       // blocks 10:00
        f.Booking(artist, At(Monday, 11, "UTC"), 60, AppointmentStatus.Cancelled);          // frees 11:00
        f.Booking(artist, At(Monday, 12, "UTC"), 60, deleted: true);                        // frees 12:00
        f.Booking(artist, At(Monday, 13, "UTC"), 60, studioId: Guid.NewGuid());             // another studio's row

        AvailableSlotsDay day = (await f.Find(artist, Monday, Monday, 60)).Days.Single();

        day.Starts.Should().Equal(At(Monday, 11, "UTC"), At(Monday, 12, "UTC"), At(Monday, 13, "UTC"));
    }

    [Fact]
    public async Task The_appointment_being_moved_does_not_block_its_own_slot()
    {
        Fixture f = Fixture.Create("UTC");
        f.Open(Monday.DayOfWeek, 10, 12);
        Guid artist = f.Artist(Monday.DayOfWeek, 10, 12);
        Guid moving = f.Booking(artist, At(Monday, 10, "UTC"), 60);

        (await f.Find(artist, Monday, Monday, 60)).Days.Single().Starts.Should().Equal(At(Monday, 11, "UTC"));
        (await f.Find(artist, Monday, Monday, 60, exclude: moving)).Days.Single().Starts
            .Should().Equal(At(Monday, 10, "UTC"), At(Monday, 11, "UTC"));
    }

    [Fact]
    public async Task Any_artist_is_the_union_and_a_start_split_between_two_artists_is_not_offered()
    {
        Fixture f = Fixture.Create("UTC");
        f.Open(Monday.DayOfWeek, 10, 18);
        f.Artist(Monday.DayOfWeek, 10, 14);
        f.Artist(Monday.DayOfWeek, 14, 18);
        Guid inactive = f.Artist(Monday.DayOfWeek, 13, 16);
        (await f.Db.Artists.FindAsync(inactive))!.IsActive = false;
        await f.Db.SaveChangesAsync();

        AvailableSlotsDay day = (await f.Find(null, Monday, Monday, 180)).Days.Single();

        // 10:00 (first artist), 14:00 (second). 13:00 would need 13-16, which neither active artist covers alone;
        // the inactive artist's 13:00 is not offered because "any artist" means active artists.
        day.Starts.Should().Equal(At(Monday, 10, "UTC"), At(Monday, 14, "UTC"));
    }

    [Fact]
    public async Task No_availability_at_all_is_a_usable_empty_answer()
    {
        Fixture f = Fixture.Create("UTC");

        AvailableSlotsResult result = await f.Find(null, Monday, Monday.AddDays(13), 60);

        result.Days.Should().HaveCount(14).And.AllSatisfy(d => d.Starts.Should().BeEmpty());
        result.NextAvailable.Should().BeNull();
    }

    [Fact]
    public async Task Starts_at_or_before_the_earliest_allowed_are_not_offered()
    {
        Fixture f = Fixture.Create("UTC");
        f.Open(Monday.DayOfWeek, 10, 14);
        Guid artist = f.Artist(Monday.DayOfWeek, 10, 14);

        AvailableSlotsResult result = await f.Find(artist, Monday, Monday, 60, earliest: At(Monday, 11, "UTC"));

        result.Days.Single().Starts.Should().Equal(At(Monday, 12, "UTC"), At(Monday, 13, "UTC"));
    }

    // ── The studio's calendar, never UTC's or the browser's ───────────────────────────────────

    [Theory]
    [InlineData(2027, 3, 27, 9)]   // Saturday before EU spring forward: 10:00 CET = 09:00 UTC
    [InlineData(2027, 3, 28, 8)]   // spring forward day: 10:00 CEST = 08:00 UTC
    [InlineData(2027, 10, 30, 8)]  // Saturday before fall back: 10:00 CEST = 08:00 UTC
    [InlineData(2027, 10, 31, 9)]  // fall back day: 10:00 CET = 09:00 UTC
    public async Task Opening_time_follows_the_studio_clock_across_both_DST_changes(int y, int m, int d, int utcHour)
    {
        DateOnly date = new(y, m, d);
        Fixture f = Fixture.Create("Europe/Paris");
        f.Open(date.DayOfWeek, 10, 12);
        Guid artist = f.Artist(date.DayOfWeek, 10, 12);

        AvailableSlotsDay day = (await f.Find(artist, date, date, 120)).Days.Single();

        day.Starts.Should().Equal(new DateTime(y, m, d, utcHour, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task A_day_is_the_studios_day_even_when_its_starts_fall_on_the_previous_UTC_date()
    {
        DateOnly date = new(2027, 6, 7);
        Fixture f = Fixture.Create("Pacific/Auckland"); // UTC+12 in June
        f.Open(date.DayOfWeek, 8, 10);
        Guid artist = f.Artist(date.DayOfWeek, 8, 10);

        AvailableSlotsDay day = (await f.Find(artist, date, date, 60)).Days.Single();

        day.Date.Should().Be(date);
        day.Starts.Should().Equal(
            new DateTime(2027, 6, 6, 20, 0, 0, DateTimeKind.Utc),
            new DateTime(2027, 6, 6, 21, 0, 0, DateTimeKind.Utc));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────────────────────

    private static DateTime Day(DateOnly d) => d.ToDateTime(TimeOnly.MinValue);

    private static DateTime At(DateOnly d, int hour, string zone) =>
        TimezoneUtils.StudioLocalToUtcForRules(d.ToDateTime(new TimeOnly(hour, 0)), zone);

    private sealed class Fixture
    {
        public FakeDbContext Db { get; } = FakeDbContext.Create();
        public Guid StudioId { get; } = Guid.NewGuid();

        public static Fixture Create(string zone)
        {
            Fixture f = new();
            f.Db.Studios.Add(new Studio { Id = f.StudioId, Name = "S", Slug = $"s-{f.StudioId:N}", OwnerEmail = "o@x.test", Timezone = zone });
            f.Db.SaveChanges();
            return f;
        }

        public void Open(DayOfWeek day, int fromHour, int toHour)
        {
            Db.StudioHours.Add(new StudioHours
            {
                StudioId = StudioId,
                DayOfWeek = day,
                IsOpen = true,
                StartTime = TimeSpan.FromHours(fromHour),
                EndTime = TimeSpan.FromHours(toHour),
            });
            Db.SaveChanges();
        }

        public Guid Artist(DayOfWeek day, int fromHour, int toHour)
        {
            Artist artist = new() { StudioId = StudioId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid()}@a.test" };
            Db.Artists.Add(artist);
            Db.SaveChanges();
            Schedule(artist.Id, day, fromHour, toHour);
            return artist.Id;
        }

        public void Schedule(Guid artistId, DayOfWeek day, int fromHour, int toHour)
        {
            Db.ArtistSchedules.Add(new ArtistSchedule
            {
                StudioId = StudioId,
                ArtistId = artistId,
                DayOfWeek = day,
                IsAvailable = true,
                StartTime = TimeSpan.FromHours(fromHour),
                EndTime = TimeSpan.FromHours(toHour),
            });
            Db.SaveChanges();
        }

        public Guid Booking(Guid artistId, DateTime start, int minutes,
            AppointmentStatus status = AppointmentStatus.Confirmed, bool deleted = false, Guid? studioId = null)
        {
            Appointment a = new()
            {
                StudioId = studioId ?? StudioId,
                ArtistId = artistId,
                ClientId = Guid.NewGuid(),
                Date = start,
                EndDate = start.AddMinutes(minutes),
                DurationMinutes = minutes,
                Status = status,
                DepositStatus = DepositStatus.Pending,
                DeletedAt = deleted ? DateTime.UtcNow : null,
            };
            Db.Appointments.Add(a);
            Db.SaveChanges();
            return a.Id;
        }

        public Task<AvailableSlotsResult> Find(Guid? artistId, DateOnly from, DateOnly to, int duration,
            Guid? exclude = null, DateTime? earliest = null) =>
            Db.FindAvailableSlotsAsync(StudioId, artistId, from, to, duration, earliest ?? LongAgo, exclude, default);
    }

    /// <summary>A studio with random hours, artists, schedules, closures, time off and bookings.</summary>
    private sealed class RandomStudio
    {
        public Guid StudioId { get; } = Guid.NewGuid();
        public string Zone { get; private init; } = "UTC";
        public List<Guid> ArtistIds { get; } = [];
        public Dictionary<Guid, List<Guid>> AppointmentIdsByArtist { get; } = [];
        private readonly Dictionary<DayOfWeek, StudioHours> _hours = [];
        private readonly Dictionary<(Guid, DayOfWeek), ArtistSchedule> _schedules = [];
        private readonly HashSet<Guid> _activeArtists = [];

        public static RandomStudio Seed(FakeDbContext db, Random rng, string zone, DateOnly from)
        {
            RandomStudio s = new() { Zone = zone };
            db.Studios.Add(new Studio { Id = s.StudioId, Name = "R", Slug = $"r-{s.StudioId:N}", OwnerEmail = "o@x.test", Timezone = zone });

            TimeSpan HalfHour(int min, int max) => TimeSpan.FromMinutes(30 * rng.Next(min * 2, max * 2 + 1));

            foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
            {
                StudioHours h = new()
                {
                    StudioId = s.StudioId,
                    DayOfWeek = day,
                    IsOpen = rng.Next(6) != 0,
                    StartTime = HalfHour(7, 11),
                    EndTime = HalfHour(15, 22),
                };
                db.StudioHours.Add(h);
                s._hours[day] = h;
            }

            int artistCount = rng.Next(1, 4);
            for (int i = 0; i < artistCount; i++)
            {
                Artist artist = new()
                {
                    StudioId = s.StudioId,
                    FirstName = "A",
                    LastName = $"{i}",
                    Email = $"{Guid.NewGuid()}@a.test",
                    IsActive = rng.Next(5) != 0,
                };
                db.Artists.Add(artist);
                s.ArtistIds.Add(artist.Id);
                if (artist.IsActive) s._activeArtists.Add(artist.Id);
                foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
                {
                    if (rng.Next(4) == 0) continue;
                    ArtistSchedule sched = new()
                    {
                        StudioId = s.StudioId,
                        ArtistId = artist.Id,
                        DayOfWeek = day,
                        IsAvailable = rng.Next(6) != 0,
                        StartTime = HalfHour(6, 12),
                        EndTime = HalfHour(14, 23),
                    };
                    db.ArtistSchedules.Add(sched);
                    if (sched.IsAvailable) s._schedules[(artist.Id, day)] = sched;
                }

                for (int t = rng.Next(0, 3); t > 0; t--)
                {
                    DateOnly start = from.AddDays(rng.Next(-2, 8));
                    db.ArtistTimeOffs.Add(new ArtistTimeOff
                    {
                        StudioId = s.StudioId,
                        ArtistId = artist.Id,
                        StartDate = start.ToDateTime(TimeOnly.MinValue),
                        EndDate = start.AddDays(rng.Next(0, 3)).ToDateTime(TimeOnly.MinValue),
                        DeletedAt = rng.Next(5) == 0 ? DateTime.UtcNow : null,
                    });
                }

                for (int b = rng.Next(0, 12); b > 0; b--)
                {
                    DateOnly day = from.AddDays(rng.Next(-1, 8));
                    DateTime start = TimezoneUtils.StudioLocalToUtcForRules(
                        day.ToDateTime(TimeOnly.MinValue).AddMinutes(15 * rng.Next(28, 84)), zone);
                    int minutes = new[] { 30, 45, 60, 90, 120, 180, 240 }[rng.Next(7)];
                    Appointment a = new()
                    {
                        StudioId = rng.Next(10) == 0 ? Guid.NewGuid() : s.StudioId,
                        ArtistId = artist.Id,
                        ClientId = Guid.NewGuid(),
                        Date = start,
                        EndDate = start.AddMinutes(minutes),
                        DurationMinutes = minutes,
                        Status = rng.Next(5) == 0 ? AppointmentStatus.Cancelled : AppointmentStatus.Confirmed,
                        DepositStatus = DepositStatus.Pending,
                        DeletedAt = rng.Next(8) == 0 ? DateTime.UtcNow : null,
                    };
                    db.Appointments.Add(a);
                    if (!s.AppointmentIdsByArtist.TryGetValue(artist.Id, out List<Guid>? ids))
                        s.AppointmentIdsByArtist[artist.Id] = ids = [];
                    ids.Add(a.Id);
                }
            }

            for (int c = rng.Next(0, 3); c > 0; c--)
            {
                DateOnly start = from.AddDays(rng.Next(-2, 8));
                db.StudioClosures.Add(new StudioClosure
                {
                    StudioId = rng.Next(6) == 0 ? Guid.NewGuid() : s.StudioId,
                    StartDate = start.ToDateTime(TimeOnly.MinValue),
                    EndDate = start.AddDays(rng.Next(0, 2)).ToDateTime(TimeOnly.MinValue),
                    Reason = "Closed",
                    DeletedAt = rng.Next(5) == 0 ? DateTime.UtcNow : null,
                });
            }

            db.SaveChanges();
            return s;
        }

        /// <summary>The list's candidate grid, derived from the seeded rows: duration steps from each artist's anchor.</summary>
        public IEnumerable<DateTime> GridFor(DateOnly day, int duration, Guid? artistId)
        {
            if (!_hours.TryGetValue(day.DayOfWeek, out StudioHours? h) || !h.IsOpen) yield break;
            IEnumerable<Guid> artists = artistId is Guid a ? [a] : _activeArtists;
            foreach (Guid artist in artists)
            {
                if (!_schedules.TryGetValue((artist, day.DayOfWeek), out ArtistSchedule? sched)) continue;
                TimeSpan anchor = h.StartTime > sched.StartTime ? h.StartTime : sched.StartTime;
                for (TimeSpan t = anchor; t < TimeSpan.FromDays(1); t += TimeSpan.FromMinutes(duration))
                    yield return TimezoneUtils.StudioLocalToUtcForRules(day.ToDateTime(TimeOnly.MinValue) + t, Zone);
            }
        }
    }
}
