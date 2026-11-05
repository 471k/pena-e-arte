using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Application.Artists.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;
using NSubstitute;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// E04c (A06) on real MySQL. The affected-session check's overlap comparison runs as SQL against
/// DATETIME columns, so the boundary cases are proved here and not only in memory: a session starting
/// before the range and ending inside it, the reverse, and sessions touching a boundary exactly. And
/// adding or removing time off leaves every appointment row exactly as it was — asserted column by
/// column against a snapshot, not inferred from the absence of an error.
///
/// Studio timezone Europe/Tirane (UTC+1 in November): the range 10–12 Nov 2026 is the UTC window
/// [9 Nov 23:00, 12 Nov 23:00).
/// </summary>
[Collection("Database")]
public class TimeOffAffectedSessionsIntegrationTests(DatabaseFixture fixture)
{
    private static readonly DateTime From = new(2026, 11, 10);
    private static readonly DateTime To = new(2026, 11, 12);

    private static DateTime Utc(int day, int hour, int minute = 0) => new(2026, 11, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public async Task BoundaryOverlap_IsDecidedCorrectly_InSql()
    {
        Seeded s = await Seed();
        Guid startsBeforeEndsInside = await AddAppointment(s, Utc(9, 21, 30), Utc(10, 0, 30));
        Guid startsInsideEndsAfter = await AddAppointment(s, Utc(12, 22), Utc(13, 1));
        Guid inside = await AddAppointment(s, Utc(11, 10), Utc(11, 12));
        Guid spansAll = await AddAppointment(s, Utc(9, 12), Utc(13, 12));
        await AddAppointment(s, Utc(9, 22), Utc(9, 23));            // ends exactly at the first local midnight
        await AddAppointment(s, Utc(12, 23), Utc(13, 1));           // starts exactly at the midnight after the last day
        await AddAppointment(s, Utc(11, 10), Utc(11, 12), AppointmentStatus.Cancelled);
        await AddAppointment(s, Utc(11, 13), Utc(11, 14), AppointmentStatus.Completed);
        await AddAppointment(s, Utc(11, 15), Utc(11, 16), AppointmentStatus.NoShow);
        await AddAppointment(s, Utc(11, 10), Utc(11, 12), artistId: s.OtherArtistId);

        TimeOffAffectedSessionsResponse r = await ReadAffected(s);

        r.Sessions.Select(x => x.AppointmentId).Should().Equal(spansAll, startsBeforeEndsInside, inside, startsInsideEndsAfter);
        r.Sessions.Should().OnlyContain(x => x.ClientName == "Mira Hoxha");
    }

    [Fact]
    public async Task AddingTimeOff_OverBookedSessions_LeavesEveryAppointmentRowUnmodified()
    {
        Seeded s = await Seed();
        await AddAppointment(s, Utc(10, 9), Utc(10, 11), AppointmentStatus.Pending);
        await AddAppointment(s, Utc(11, 10), Utc(11, 12));
        await AddAppointment(s, Utc(9, 21, 30), Utc(10, 0, 30));
        await AddAppointment(s, Utc(20, 10), Utc(20, 12)); // outside the range, as a control

        List<Appointment> before = await Snapshot(s);
        (await ReadAffected(s)).Sessions.Should().HaveCount(3, "the check sees them");

        Guid timeOffId;
        await using (AppDbContext db = fixture.CreateDbContext(s.StudioId))
        {
            timeOffId = await new AddArtistTimeOffHandler(db, Tenant(s), Artist(s))
                .Handle(new AddArtistTimeOffCommand(s.ArtistId, From, To, "Holiday"), default);
        }

        List<Appointment> afterAdd = await Snapshot(s);
        afterAdd.Should().BeEquivalentTo(before, o => o.WithStrictOrdering(), "adding time off must not touch any appointment");

        await using (AppDbContext db = fixture.CreateDbContext(s.StudioId))
        {
            (await db.ArtistTimeOffs.AsNoTracking().SingleAsync(t => t.Id == timeOffId)).StartDate.Should().Be(From);
            await new DeleteArtistTimeOffHandler(db, Artist(s))
                .Handle(new DeleteArtistTimeOffCommand(s.ArtistId, timeOffId), default);
        }

        List<Appointment> afterDelete = await Snapshot(s);
        afterDelete.Should().BeEquivalentTo(before, o => o.WithStrictOrdering(), "removing time off must not touch any appointment either");
    }

    // Every column of every appointment in the studio, read fresh (no tracking), in a stable order.
    private async Task<List<Appointment>> Snapshot(Seeded s)
    {
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        List<Appointment> rows = await db.Appointments.IgnoreQueryFilters().AsNoTracking()
            .Where(a => a.StudioId == s.StudioId)
            .OrderBy(a => a.Date).ThenBy(a => a.Id)
            .ToListAsync();
        rows.Should().NotBeEmpty();
        return rows;
    }

    private async Task<TimeOffAffectedSessionsResponse> ReadAffected(Seeded s)
    {
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        return await new GetTimeOffAffectedSessionsHandler(db, Artist(s))
            .Handle(new GetTimeOffAffectedSessionsQuery(s.ArtistId, From, To), default);
    }

    private static CurrentTenantService Tenant(Seeded s)
    {
        CurrentTenantService tenant = new();
        tenant.SetTenant(s.StudioId);
        return tenant;
    }

    private static ICurrentUser Artist(Seeded s)
    {
        ICurrentUser user = Substitute.For<ICurrentUser>();
        user.Role.Returns("artist");
        user.UserId.Returns(s.ArtistUserId);
        return user;
    }

    private sealed record Seeded(Guid StudioId, Guid ArtistId, Guid ArtistUserId, Guid OtherArtistId, Guid ClientId);

    private async Task<Seeded> Seed()
    {
        Guid studioId = Guid.NewGuid();
        Guid artistUserId = Guid.NewGuid();
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        ctx.Studios.Add(new Studio
        {
            Id = studioId, Name = "TimeOff", Slug = studioId.ToString("N")[..8], CountryCode = "AL", Currency = "ALL",
            Timezone = "Europe/Tirane",
        });
        Artist artist = new() { StudioId = studioId, UserId = artistUserId, FirstName = "Ari", LastName = "Kola", Email = $"{Guid.NewGuid():N}@a.test", IsActive = true };
        Artist other = new() { StudioId = studioId, UserId = Guid.NewGuid(), FirstName = "Oth", LastName = "Er", Email = $"{Guid.NewGuid():N}@a.test", IsActive = true };
        Client client = new() { StudioId = studioId, FirstName = "Mira", LastName = "Hoxha", Email = $"{Guid.NewGuid():N}@c.test" };
        ctx.Artists.AddRange(artist, other);
        ctx.Clients.Add(client);
        await ctx.SaveChangesAsync();
        return new Seeded(studioId, artist.Id, artistUserId, other.Id, client.Id);
    }

    private async Task<Guid> AddAppointment(Seeded s, DateTime startUtc, DateTime endUtc,
        AppointmentStatus status = AppointmentStatus.Confirmed, Guid? artistId = null)
    {
        await using AppDbContext ctx = fixture.CreateDbContext(s.StudioId);
        Appointment a = new()
        {
            StudioId = s.StudioId,
            ArtistId = artistId ?? s.ArtistId,
            ClientId = s.ClientId,
            Date = startUtc,
            EndDate = endUtc,
            DurationMinutes = (int)(endUtc - startUtc).TotalMinutes,
            Status = status,
            DepositStatus = DepositStatus.Pending,
            DepositAmount = 20m,
            Notes = "keep me",
        };
        ctx.Appointments.Add(a);
        await ctx.SaveChangesAsync();
        return a.Id;
    }
}
