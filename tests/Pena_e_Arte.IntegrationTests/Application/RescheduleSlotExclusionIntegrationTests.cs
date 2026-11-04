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
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// E04b (A02) on real MySQL: excluding the appointment being moved lets it move within its own slot,
/// a real overlap is still rejected, the save agrees with the preview in both cases, and a caller
/// can't exclude an appointment they may not move.
/// </summary>
[Collection("Database")]
public class RescheduleSlotExclusionIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task TheAppointmentNoLongerBlocksItsOwnMove_AndTheSaveAgrees()
    {
        Seeded s = await Seed();
        DateTime newStart = s.MovingStart.AddHours(1); // overlaps its own current 2 h slot

        SlotAvailabilityResult without = await Check(s, Staff(), newStart, 120, exclude: null);
        SlotAvailabilityResult with = await Check(s, Staff(), newStart, 120, exclude: s.Moving);

        without.Available.Should().BeFalse("the old check counted the appointment against itself");
        with.Available.Should().BeTrue();
        await Reschedule(s, Staff(), s.Moving, newStart, 120);
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        (await db.Appointments.FindAsync(s.Moving))!.Date.Should().BeCloseTo(newStart, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ARealOverlap_IsStillRejected_ByThePreviewAndTheSave()
    {
        Seeded s = await Seed();

        SlotAvailabilityResult preview = await Check(s, Staff(), s.OtherStart, 60, exclude: s.Moving);
        Func<Task> save = () => Reschedule(s, Staff(), s.Moving, s.OtherStart, 60);

        preview.Available.Should().BeFalse();
        preview.Reason.Should().Be("That slot is already booked.");
        await save.Should().ThrowAsync<SlotAlreadyBookedException>();
    }

    [Fact]
    public async Task AClient_CannotExcludeAnotherClientsAppointment_ButCanExcludeTheirOwn()
    {
        Seeded s = await Seed();

        Func<Task> other = () => Check(s, Client(s.OtherClientUserId), s.MovingStart.AddHours(1), 120, exclude: s.Moving);
        SlotAvailabilityResult own = await Check(s, Client(s.MovingClientUserId), s.MovingStart.AddHours(1), 120, exclude: s.Moving);

        await other.Should().ThrowAsync<NotFoundException>();
        own.Available.Should().BeTrue();
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static ICurrentUser Staff()
    {
        ICurrentUser u = Substitute.For<ICurrentUser>();
        u.Role.Returns("owner");
        u.UserId.Returns(Guid.NewGuid());
        return u;
    }

    private static ICurrentUser Client(Guid userId)
    {
        ICurrentUser u = Substitute.For<ICurrentUser>();
        u.Role.Returns("client");
        u.UserId.Returns(userId);
        return u;
    }

    private async Task<SlotAvailabilityResult> Check(Seeded s, ICurrentUser user, DateTime start, int minutes, Guid? exclude)
    {
        CurrentTenantService tenant = new();
        tenant.SetTenant(s.StudioId);
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        return await new CheckSlotAvailabilityHandler(db, tenant, user)
            .Handle(new CheckSlotAvailabilityQuery(s.ArtistId, start, minutes, exclude), default);
    }

    private async Task Reschedule(Seeded s, ICurrentUser user, Guid id, DateTime start, int minutes)
    {
        CurrentTenantService tenant = new();
        tenant.SetTenant(s.StudioId);
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        await new RescheduleAppointmentHandler(
                db, tenant, user, Substitute.For<IRealtimeNotifier>(), Substitute.For<ISender>(), Substitute.For<IJobScheduler>())
            .Handle(new RescheduleAppointmentCommand(id, new RescheduleAppointmentRequest(start, minutes, null)), default);
    }

    private sealed record Seeded(
        Guid StudioId, Guid ArtistId, Guid Moving, DateTime MovingStart, DateTime OtherStart,
        Guid MovingClientUserId, Guid OtherClientUserId);

    private async Task<Seeded> Seed()
    {
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name = "Move Studio",
            Slug = "move-" + Guid.NewGuid().ToString("N")[..8],
            City = "Tirana",
            OwnerEmail = $"owner-{Guid.NewGuid():N}@example.test",
            CountryCode = "AL",
            Currency = "EUR",
            Timezone = "UTC",
            IsActive = true,
            IsPublished = true,
        };
        seed.Studios.Add(studio);
        Artist artist = new() { StudioId = studio.Id, FirstName = "Jane", LastName = "Doe", Email = $"artist-{Guid.NewGuid():N}@test.com", IsActive = true };
        Guid movingUser = Guid.NewGuid();
        Guid otherUser = Guid.NewGuid();
        Client moving = new() { StudioId = studio.Id, UserId = movingUser, FirstName = "M", LastName = "V", Email = $"{Guid.NewGuid():N}@m.test" };
        Client other = new() { StudioId = studio.Id, UserId = otherUser, FirstName = "O", LastName = "T", Email = $"{Guid.NewGuid():N}@o.test" };
        seed.Artists.Add(artist);
        seed.Clients.AddRange(moving, other);
        foreach (DayOfWeek day in Enum.GetValues<DayOfWeek>())
        {
            seed.ArtistSchedules.Add(new ArtistSchedule { StudioId = studio.Id, ArtistId = artist.Id, DayOfWeek = day, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 59, 0), IsAvailable = true });
            seed.StudioHours.Add(new StudioHours { StudioId = studio.Id, DayOfWeek = day, StartTime = TimeSpan.Zero, EndTime = new TimeSpan(23, 59, 0), IsOpen = true });
        }
        DateTime day7 = DateTime.UtcNow.Date.AddDays(7);
        DateTime movingStart = day7.AddHours(10);
        DateTime otherStart = day7.AddHours(14);
        Appointment a = new() { StudioId = studio.Id, ArtistId = artist.Id, ClientId = moving.Id, Date = movingStart, EndDate = movingStart.AddHours(2), DurationMinutes = 120, Status = AppointmentStatus.Confirmed };
        Appointment b = new() { StudioId = studio.Id, ArtistId = artist.Id, ClientId = other.Id, Date = otherStart, EndDate = otherStart.AddHours(1), DurationMinutes = 60, Status = AppointmentStatus.Confirmed };
        seed.Appointments.AddRange(a, b);
        await seed.SaveChangesAsync();
        return new Seeded(studio.Id, artist.Id, a.Id, movingStart, otherStart, movingUser, otherUser);
    }
}
