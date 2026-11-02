using System.Collections.Concurrent;
using System.Data.Common;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Commands;
using Pena_e_Arte.Application.Public.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// DEC09: two bookings for one artist must never both land in overlapping time, whether or not Redis
/// is up. Real MySQL, two DbContexts on two connections, run in parallel.
///
/// The race is forced, not hoped for: <see cref="WriteRendezvous"/> holds each request at its write to
/// `appointments` until the other request reaches its own write too (or a few seconds pass). Without
/// the database guard both requests pass their overlap read, meet at the rendezvous and both write —
/// two overlapping rows (verified by running these tests with the claim calls removed). With the guard
/// the second request is blocked on the artist lock before its overlap read, so it never reaches the
/// rendezvous while the first holds the lock; the first writes after the timeout and commits, and the
/// second then sees it and gets SlotAlreadyBookedException.
/// </summary>
[Collection("Database")]
public class BookingRaceIntegrationTests(DatabaseFixture fixture)
{
    private static readonly TimeSpan RendezvousTimeout = TimeSpan.FromSeconds(3);

    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IPlanLimitService _planLimits = Substitute.For<IPlanLimitService>();

    // ── CreateAppointmentCommand ──────────────────────────────────────────────

    [Fact]
    public async Task OverlappingDifferentStarts_RedisHealthy_ExactlyOneBooks()
    {
        // A healthy Redis lock is keyed on the exact start minute, so 10:00 and 10:30 take different
        // keys and both get through it — the overlap is caught only by the database.
        (Guid studioId, Guid artistId, Guid clientA, Guid clientB) = await SeedStudio();
        DateTime ten = NextMondayAt(10);

        await AssertExactlyOneBooks(studioId, artistId, new ExactMinuteLocker(),
            (clientA, ten, 90), (clientB, ten.AddMinutes(30), 90));
    }

    [Fact]
    public async Task OverlappingDifferentStarts_RedisDown_ExactlyOneBooks()
    {
        // The regression test for SlotLocker's fail-open: it returns true for everyone.
        (Guid studioId, Guid artistId, Guid clientA, Guid clientB) = await SeedStudio();
        DateTime ten = NextMondayAt(10);

        await AssertExactlyOneBooks(studioId, artistId, AlwaysGrantingLocker(),
            (clientA, ten, 90), (clientB, ten.AddMinutes(30), 90));
    }

    [Fact]
    public async Task SameStart_RedisDown_ExactlyOneBooks()
    {
        (Guid studioId, Guid artistId, Guid clientA, Guid clientB) = await SeedStudio();
        DateTime ten = NextMondayAt(10);

        await AssertExactlyOneBooks(studioId, artistId, AlwaysGrantingLocker(),
            (clientA, ten, 60), (clientB, ten, 60));
    }

    [Fact]
    public async Task NonOverlapping_RedisDown_BothBook()
    {
        // The lock serialises bookings for one artist; it must not turn a free slot into a conflict.
        (Guid studioId, Guid artistId, Guid clientA, Guid clientB) = await SeedStudio();
        DateTime ten = NextMondayAt(10);
        WriteRendezvous gate = new("INSERT INTO `appointments`", RendezvousTimeout);
        ISlotLocker locker = AlwaysGrantingLocker();

        Exception?[] outcomes = await Task.WhenAll(
            Capture(() => RunCreate(studioId, gate, locker, new CreateAppointmentRequest(artistId, clientA, ten, 60, null))),
            Capture(() => RunCreate(studioId, gate, locker, new CreateAppointmentRequest(artistId, clientB, ten.AddHours(2), 60, null))));

        outcomes.Should().AllSatisfy(e => e.Should().BeNull());
        (await LiveAppointmentCount(studioId, artistId)).Should().Be(2);
    }

    // ── CreateGuestAppointmentCommand (anonymous, no tenant) ─────────────────

    [Fact]
    public async Task Guest_OverlappingDifferentStarts_RedisDown_ExactlyOneBooks()
    {
        (Guid studioId, Guid artistId, _, _) = await SeedStudio();
        string slug = await SlugOf(studioId);
        DateTime ten = NextMondayAt(10);
        WriteRendezvous gate = new("INSERT INTO `appointments`", RendezvousTimeout);
        ISlotLocker locker = AlwaysGrantingLocker();

        Exception?[] outcomes = await Task.WhenAll(
            Capture(() => RunGuest(slug, gate, locker, artistId, ten)),
            Capture(() => RunGuest(slug, gate, locker, artistId, ten.AddMinutes(30))));

        AssertOneWonOneLost(outcomes);
        (await LiveAppointmentCount(studioId, artistId)).Should().Be(1);
    }

    // ── RescheduleAppointmentCommand (moving into an artist's time) ──────────

    [Fact]
    public async Task Reschedule_TwoMovesIntoOverlappingTime_ExactlyOneLands()
    {
        (Guid studioId, Guid artistId, Guid clientA, Guid clientB) = await SeedStudio();
        DateTime ten = NextMondayAt(10);
        Guid first = await SeedAppointment(studioId, artistId, clientA, ten.AddDays(1));
        Guid second = await SeedAppointment(studioId, artistId, clientB, ten.AddDays(2));
        WriteRendezvous gate = new("UPDATE `appointments`", RendezvousTimeout);

        Exception?[] outcomes = await Task.WhenAll(
            Capture(() => RunReschedule(studioId, gate, first, ten, 90)),
            Capture(() => RunReschedule(studioId, gate, second, ten.AddMinutes(30), 90)));

        AssertOneWonOneLost(outcomes);
        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        int inTheContestedWindow = await verify.Appointments.CountAsync(a =>
            a.ArtistId == artistId && a.Date < ten.AddMinutes(120) && a.EndDate > ten);
        inTheContestedWindow.Should().Be(1);
    }

    // ── AssignAppointmentArtistCommand (the studio-choice path's real claim) ─

    [Fact]
    public async Task Assign_TwoUnassignedRequestsToOneArtistInOverlappingTime_ExactlyOneIsAssigned()
    {
        (Guid studioId, Guid artistId, Guid clientA, Guid clientB) = await SeedStudio();
        DateTime ten = NextMondayAt(10);
        Guid first = await SeedAppointment(studioId, null, clientA, ten, minutes: 90);
        Guid second = await SeedAppointment(studioId, null, clientB, ten.AddMinutes(30), minutes: 90);
        WriteRendezvous gate = new("UPDATE `appointments`", RendezvousTimeout);
        ISlotLocker locker = AlwaysGrantingLocker();

        Exception?[] outcomes = await Task.WhenAll(
            Capture(() => RunAssign(studioId, gate, locker, first, artistId)),
            Capture(() => RunAssign(studioId, gate, locker, second, artistId)));

        AssertOneWonOneLost(outcomes);
        (await LiveAppointmentCount(studioId, artistId)).Should().Be(1);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task AssertExactlyOneBooks(
        Guid studioId, Guid artistId, ISlotLocker locker,
        (Guid ClientId, DateTime Start, int Minutes) a, (Guid ClientId, DateTime Start, int Minutes) b)
    {
        WriteRendezvous gate = new("INSERT INTO `appointments`", RendezvousTimeout);

        Exception?[] outcomes = await Task.WhenAll(
            Capture(() => RunCreate(studioId, gate, locker, new CreateAppointmentRequest(artistId, a.ClientId, a.Start, a.Minutes, null))),
            Capture(() => RunCreate(studioId, gate, locker, new CreateAppointmentRequest(artistId, b.ClientId, b.Start, b.Minutes, null))));

        AssertOneWonOneLost(outcomes);
        (await LiveAppointmentCount(studioId, artistId)).Should().Be(1);
    }

    private static void AssertOneWonOneLost(Exception?[] outcomes)
    {
        outcomes.Count(e => e is null).Should().Be(1, "exactly one booking may succeed; got {0}",
            string.Join(" | ", outcomes.Select(e => e?.GetType().Name ?? "success")));
        outcomes.Count(e => e is SlotAlreadyBookedException).Should().Be(1);
    }

    private static async Task<Exception?> Capture(Func<Task> work)
    {
        // Task.Run: each request starts on its own thread, as two HTTP requests would.
        try { await Task.Run(work); return null; }
        catch (Exception ex) { return ex; }
    }

    private AppDbContext CreateGatedDbContext(CurrentTenantService tenant, WriteRendezvous gate)
    {
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0)))
            .AddInterceptors(gate)
            .Options;
        return new AppDbContext(options, tenant);
    }

    private async Task RunCreate(Guid studioId, WriteRendezvous gate, ISlotLocker locker, CreateAppointmentRequest req)
    {
        CurrentTenantService tenant = new();
        tenant.SetTenant(studioId);
        ICurrentUser staff = Substitute.For<ICurrentUser>();
        staff.Role.Returns("owner");
        await using AppDbContext db = CreateGatedDbContext(tenant, gate);
        CreateAppointmentHandler handler = new(db, tenant, staff, locker, _jobs, _realtime, _sender, _planLimits);
        await handler.Handle(new CreateAppointmentCommand(req), default);
    }

    private async Task RunGuest(string slug, WriteRendezvous gate, ISlotLocker locker, Guid artistId, DateTime start)
    {
        string email = $"guest-{Guid.NewGuid():N}@example.test";
        IIdentityService identity = Substitute.For<IIdentityService>();
        identity.GetUserIdByEmailAsync(email, Arg.Any<CancellationToken>()).Returns((Guid?)null);
        identity.CreateUserAsync(email, Arg.Any<string>(), "client", Arg.Any<Guid>(), Arg.Any<string>())
            .Returns((true, Guid.NewGuid(), Array.Empty<string>()));
        identity.GeneratePasswordResetTokenAsync(email).Returns((true, "reset-token", (string?)null));
        identity.GenerateEmailConfirmationTokenAsync(Arg.Any<Guid>()).Returns("confirm-token");
        IAppSettings settings = Substitute.For<IAppSettings>();
        settings.BaseUrl.Returns("https://tattooos.co");
        IEmailRenderer renderer = Substitute.For<IEmailRenderer>();
        renderer.RenderGuestBookingWelcome(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>()).Returns("<html></html>");

        CreateGuestAppointmentRequest request = new(
            "Jamie", "Guest", email, "+351912345678", MarketingOptIn: false,
            Booking: new CreateAppointmentRequest(artistId, Guid.Empty, start, 90, null, "A small rose"));

        CurrentTenantService tenant = new();
        await using AppDbContext db = CreateGatedDbContext(tenant, gate);
        CreateGuestAppointmentHandler handler = new(
            db, tenant, identity, locker, _jobs, _realtime, _sender, _planLimits,
            renderer, Substitute.For<INotificationService>(), settings,
            NullLogger<CreateGuestAppointmentHandler>.Instance);
        await handler.Handle(new CreateGuestAppointmentCommand(slug, request), default);
    }

    private async Task RunReschedule(Guid studioId, WriteRendezvous gate, Guid appointmentId, DateTime newStart, int minutes)
    {
        CurrentTenantService tenant = new();
        tenant.SetTenant(studioId);
        ICurrentUser staff = Substitute.For<ICurrentUser>();
        staff.Role.Returns("owner");
        await using AppDbContext db = CreateGatedDbContext(tenant, gate);
        RescheduleAppointmentHandler handler = new(db, tenant, staff, _realtime, _sender, _jobs);
        await handler.Handle(new RescheduleAppointmentCommand(
            appointmentId, new RescheduleAppointmentRequest(newStart, minutes, null)), default);
    }

    private async Task RunAssign(Guid studioId, WriteRendezvous gate, ISlotLocker locker, Guid appointmentId, Guid artistId)
    {
        CurrentTenantService tenant = new();
        tenant.SetTenant(studioId);
        await using AppDbContext db = CreateGatedDbContext(tenant, gate);
        AssignAppointmentArtistHandler handler = new(db, tenant, locker, _realtime, _sender);
        await handler.Handle(new AssignAppointmentArtistCommand(
            appointmentId, new AssignAppointmentArtistRequest(artistId)), default);
    }

    private async Task<int> LiveAppointmentCount(Guid studioId, Guid artistId)
    {
        await using AppDbContext verify = fixture.CreateDbContext(studioId);
        return await verify.Appointments.CountAsync(a => a.ArtistId == artistId && a.Status != AppointmentStatus.Cancelled);
    }

    private async Task<string> SlugOf(Guid studioId)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        return await db.Studios.Where(s => s.Id == studioId).Select(s => s.Slug).SingleAsync();
    }

    private async Task<(Guid StudioId, Guid ArtistId, Guid ClientA, Guid ClientB)> SeedStudio()
    {
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = "Race Studio",
            Slug = "race-" + Guid.NewGuid().ToString("N")[..8],
            City = "Tirana",
            OwnerEmail = $"owner-{Guid.NewGuid():N}@example.test",
            CountryCode = "AL",
            Currency = "EUR",
            IsActive = true,
            IsPublished = true,
        };
        seed.Studios.Add(studio);
        Artist artist = new()
        {
            StudioId = studio.Id,
            FirstName = "Jane",
            LastName = "Doe",
            Email = $"artist-{Guid.NewGuid():N}@test.com",
            IsActive = true,
        };
        Client a = new() { StudioId = studio.Id, FirstName = "A", LastName = "A", Email = $"{Guid.NewGuid():N}@a.test" };
        Client b = new() { StudioId = studio.Id, FirstName = "B", LastName = "B", Email = $"{Guid.NewGuid():N}@b.test" };
        seed.Artists.Add(artist);
        seed.Clients.AddRange(a, b);
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            seed.ArtistSchedules.Add(new ArtistSchedule
            {
                StudioId = studio.Id,
                ArtistId = artist.Id,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsAvailable = true,
            });
            seed.StudioHours.Add(new StudioHours
            {
                StudioId = studio.Id,
                DayOfWeek = day,
                StartTime = TimeSpan.Zero,
                EndTime = TimeSpan.FromHours(23).Add(TimeSpan.FromMinutes(59)),
                IsOpen = true,
            });
        }
        await seed.SaveChangesAsync();
        return (studio.Id, artist.Id, a.Id, b.Id);
    }

    private async Task<Guid> SeedAppointment(Guid studioId, Guid? artistId, Guid clientId, DateTime start, int minutes = 60)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        Appointment appointment = new()
        {
            StudioId = studioId,
            ArtistId = artistId,
            ClientId = clientId,
            Date = start,
            EndDate = start.AddMinutes(minutes),
            DurationMinutes = minutes,
            Status = artistId is null ? AppointmentStatus.Pending : AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Pending,
        };
        ctx.Appointments.Add(appointment);
        await ctx.SaveChangesAsync();
        return appointment.Id;
    }

    private static DateTime NextMondayAt(int hour)
    {
        DateTime date = DateTime.UtcNow.Date.AddDays(1);
        while (date.DayOfWeek != DayOfWeek.Monday) date = date.AddDays(1);
        return date.AddHours(hour);
    }

    private static ISlotLocker AlwaysGrantingLocker()
    {
        ISlotLocker locker = Substitute.For<ISlotLocker>();
        locker.TryAcquireLockAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
              .Returns(true);
        return locker;
    }

    /// <summary>A healthy SlotLocker: one holder per exact start minute, as the Redis key works.</summary>
    private sealed class ExactMinuteLocker : ISlotLocker
    {
        private readonly ConcurrentDictionary<string, byte> _held = new();

        private static string Key(Guid studioId, Guid artistId, DateTime date) => $"slot:{studioId}:{artistId}:{date:yyyyMMddHHmm}";

        public Task<bool> TryAcquireLockAsync(Guid studioId, Guid artistId, DateTime date, CancellationToken ct) =>
            Task.FromResult(_held.TryAdd(Key(studioId, artistId, date), 0));

        public Task ReleaseLockAsync(Guid studioId, Guid artistId, DateTime date, CancellationToken ct)
        {
            _held.TryRemove(Key(studioId, artistId, date), out _);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Holds a request at its first matching write until a second request reaches its own matching write,
    /// or the timeout passes — so two requests that both got past their overlap reads write at the same time.
    /// </summary>
    private sealed class WriteRendezvous(string commandPrefix, TimeSpan timeout) : DbCommandInterceptor
    {
        private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrived;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            await MaybeWaitAsync(command);
            return result;
        }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            await MaybeWaitAsync(command);
            return result;
        }

        private async Task MaybeWaitAsync(DbCommand command)
        {
            if (!command.CommandText.Contains(commandPrefix, StringComparison.OrdinalIgnoreCase)) return;
            if (Interlocked.Increment(ref _arrived) >= 2) _bothArrived.TrySetResult();
            await Task.WhenAny(_bothArrived.Task, Task.Delay(timeout));
        }
    }
}
