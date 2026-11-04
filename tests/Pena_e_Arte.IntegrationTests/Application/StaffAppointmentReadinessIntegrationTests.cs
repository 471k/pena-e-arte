using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// E04a Phase A against real MySQL: the staff list's readiness summary (the artist's Today view) agrees
/// with the client detail endpoint's itemised view for the same appointment, because both come from
/// AppointmentReadinessReader; and the staff list costs a fixed number of queries however many sessions
/// there are (no N+1).
/// </summary>
[Collection("Database")]
public class StaffAppointmentReadinessIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task TheStaffSummary_AgreesWithTheClientsItemisedView_ForTheSameAppointment()
    {
        Seeded s = await Seed(appointments: 2);
        Guid partlyDone = s.AppointmentIds[0];
        Guid untouched = s.AppointmentIds[1];

        await using (AppDbContext ctx = fixture.CreateDbContext(s.StudioId))
        {
            ctx.ConsentForms.Add(new ConsentForm { StudioId = s.StudioId, ClientId = s.ClientId, AppointmentId = partlyDone, SignedAt = DateTime.UtcNow, SignatureData = "C D" });
            ctx.AppointmentAttachments.Add(new AppointmentAttachment { StudioId = s.StudioId, AppointmentId = partlyDone, ImageUrl = "https://r2.test/ref.jpg", Category = AppointmentAttachmentCategory.Reference });
            await ctx.SaveChangesAsync();
        }

        List<AppointmentResponse> staff = await StaffList(s);

        foreach (Guid id in s.AppointmentIds)
        {
            ClientAppointmentDetailResponse client = await ClientDetail(s, id);
            AppointmentReadinessSummaryResponse summary = staff.Single(a => a.Id == id).Readiness!;
            summary.Should().Be(client.Appointment.Readiness);
            summary.Required.Should().Be(client.Readiness.Count);
            summary.Satisfied.Should().Be(client.Readiness.Count(r => r.Satisfied));
        }
        staff.Single(a => a.Id == partlyDone).Readiness.Should().Be(new AppointmentReadinessSummaryResponse(4, 2));
        staff.Single(a => a.Id == untouched).Readiness.Should().Be(new AppointmentReadinessSummaryResponse(4, 0));
    }

    [Fact]
    public async Task TheStaffList_QueryCountDoesNotScaleWithSessions()
    {
        Seeded one = await Seed(appointments: 1);
        Seeded many = await Seed(appointments: 8);

        int forOne = await CountStaffListQueries(one);
        int forMany = await CountStaffListQueries(many);

        forOne.Should().BeGreaterThan(0, "the counter must actually see the commands");
        forMany.Should().Be(forOne, "readiness and the intake are read for the whole list at once, not per session");
        (await StaffList(many)).Should().HaveCount(8)
            .And.OnlyContain(a => a.Readiness != null && a.Readiness.Required == 4 && a.TattooDescription == "A swallow");
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static ICurrentUser Artist(Seeded s)
    {
        ICurrentUser user = Substitute.For<ICurrentUser>();
        user.Role.Returns("artist");
        user.UserId.Returns(s.ArtistUserId);
        return user;
    }

    private async Task<int> CountStaffListQueries(Seeded s)
    {
        CommandCounter counter = new();
        CurrentTenantService tenant = new();
        tenant.SetTenant(s.StudioId);
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0)))
            .AddInterceptors(counter)
            .Options;
        await using AppDbContext db = new(options, tenant);

        await new GetAppointmentsHandler(db, Artist(s)).Handle(new GetAppointmentsQuery(null, null, null, true), default);

        return counter.Count;
    }

    private async Task<List<AppointmentResponse>> StaffList(Seeded s)
    {
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        return await new GetAppointmentsHandler(db, Artist(s)).Handle(new GetAppointmentsQuery(null, null, null, true), default);
    }

    private async Task<ClientAppointmentDetailResponse> ClientDetail(Seeded s, Guid id)
    {
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        ICurrentUser client = Substitute.For<ICurrentUser>();
        client.Role.Returns("client");
        client.UserId.Returns(s.ClientUserId);
        return await new GetMyAppointmentHandler(
                db, client, Substitute.For<IIdentityService>(), Substitute.For<INotificationPreferenceService>())
            .Handle(new GetMyAppointmentQuery(id), default);
    }

    private sealed record Seeded(Guid StudioId, Guid ArtistUserId, Guid ClientUserId, Guid ClientId, List<Guid> AppointmentIds);

    private async Task<Seeded> Seed(int appointments)
    {
        Guid studioId = Guid.NewGuid();
        Guid artistUserId = Guid.NewGuid();
        Guid clientUserId = Guid.NewGuid();
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        ctx.Studios.Add(new Studio { Id = studioId, Name = "Today", Slug = studioId.ToString("N")[..8], CountryCode = "PT", Currency = "EUR" });
        Artist artist = new() { StudioId = studioId, UserId = artistUserId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid():N}@a.test", IsActive = true };
        Client client = new() { StudioId = studioId, UserId = clientUserId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid():N}@c.test" };
        Service service = new()
        {
            StudioId = studioId,
            Name = "Session",
            DurationMinutes = 120,
            IsActive = true,
            RequiresConsentForm = true,
            RequiresIntakeForm = true,
            RequiresReferenceImage = true,
            RequiresDepositPaid = true,
        };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        ctx.Services.Add(service);
        await ctx.SaveChangesAsync();

        List<Guid> ids = [];
        for (int i = 0; i < appointments; i++)
        {
            DateTime start = DateTime.UtcNow.AddDays(7 + i);
            Appointment appointment = new()
            {
                StudioId = studioId,
                ClientId = client.Id,
                ArtistId = artist.Id,
                ServiceId = service.Id,
                Date = start,
                EndDate = start.AddHours(2),
                DurationMinutes = 120,
                Status = AppointmentStatus.Confirmed,
                DepositStatus = DepositStatus.Pending,
                DepositAmount = 50m,
            };
            ctx.Appointments.Add(appointment);
            ctx.BookingIntakes.Add(new BookingIntake { StudioId = studioId, AppointmentId = appointment.Id, TattooDescription = "A swallow" });
            ids.Add(appointment.Id);
        }
        await ctx.SaveChangesAsync();

        return new Seeded(studioId, artistUserId, clientUserId, client.Id, ids);
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count;

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
        {
            Interlocked.Increment(ref Count);
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref Count);
            return ValueTask.FromResult(result);
        }
    }
}
