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
/// DEC03 readiness against real MySQL: derived from real ConsentForm / IntakeForm / attachment /
/// payment rows, the /mine summary agrees with GET /mine/{id} for the same appointment, and the list
/// costs a fixed number of queries however many appointments the client has (no N+1).
/// </summary>
[Collection("Database")]
public class ClientAppointmentReadinessIntegrationTests(DatabaseFixture fixture)
{
    private readonly ICurrentUser _user = Substitute.For<ICurrentUser>();

    [Fact]
    public async Task Readiness_ReflectsRealRows_AndTheListAgreesWithTheDetail()
    {
        Seeded s = await Seed(appointments: 1);
        Guid id = s.AppointmentIds[0];

        ClientAppointmentDetailResponse before = await Detail(s, id);
        before.Readiness.Select(r => (r.Key, r.Satisfied)).Should().Equal(
            ("DepositPaid", false), ("IntakeForm", false), ("ConsentForm", false), ("ReferenceImage", false));

        await using (AppDbContext ctx = fixture.CreateDbContext(s.StudioId))
        {
            ctx.ConsentForms.Add(new ConsentForm { StudioId = s.StudioId, ClientId = s.ClientId, AppointmentId = id, SignedAt = DateTime.UtcNow, SignatureData = "C D" });
            ctx.IntakeForms.Add(new IntakeForm { StudioId = s.StudioId, ClientId = s.ClientId, AppointmentId = id, SubmittedAt = DateTime.UtcNow, FormData = "{}" });
            ctx.AppointmentAttachments.Add(new AppointmentAttachment { StudioId = s.StudioId, AppointmentId = id, ImageUrl = "https://r2.test/ref.jpg", Category = AppointmentAttachmentCategory.Reference });
            // A cash promise is not money received: the deposit stays outstanding.
            ctx.Payments.Add(new Payment
            {
                StudioId = s.StudioId,
                AppointmentId = id,
                ClientId = s.ClientId,
                Amount = 50m,
                Currency = "EUR",
                Status = PaymentStatus.CashPending,
                Method = ClientPaymentMethod.Cash,
            });
            await ctx.SaveChangesAsync();
        }

        ClientAppointmentDetailResponse after = await Detail(s, id);
        after.Readiness.Select(r => (r.Key, r.Satisfied)).Should().Equal(
            ("DepositPaid", false), ("IntakeForm", true), ("ConsentForm", true), ("ReferenceImage", true));
        after.Appointment.Readiness.Should().Be(new AppointmentReadinessSummaryResponse(4, 3));

        List<AppointmentResponse> list = await Mine(s);
        list.Single(a => a.Id == id).Readiness.Should().Be(after.Appointment.Readiness);

        // The studio confirms the cash: now the deposit counts.
        await using (AppDbContext ctx = fixture.CreateDbContext(s.StudioId))
        {
            Payment payment = await ctx.Payments.SingleAsync(p => p.AppointmentId == id);
            payment.Status = PaymentStatus.Paid;
            Appointment appointment = await ctx.Appointments.SingleAsync(a => a.Id == id);
            appointment.DepositStatus = DepositStatus.Paid;
            await ctx.SaveChangesAsync();
        }

        (await Detail(s, id)).Readiness.Should().OnlyContain(r => r.Satisfied);
        (await Mine(s)).Single(a => a.Id == id).Readiness.Should().Be(new AppointmentReadinessSummaryResponse(4, 4));
    }

    [Fact]
    public async Task ServiceWithNoRequirements_GivesNoChecklist()
    {
        Seeded s = await Seed(appointments: 1, requirements: false);

        ClientAppointmentDetailResponse detail = await Detail(s, s.AppointmentIds[0]);

        detail.Readiness.Should().BeEmpty();
        detail.Appointment.Readiness.Should().Be(new AppointmentReadinessSummaryResponse(0, 0));
    }

    [Fact]
    public async Task TheBookingsList_QueryCountDoesNotScaleWithRows()
    {
        Seeded one = await Seed(appointments: 1);
        Seeded many = await Seed(appointments: 8);

        int forOne = await CountListQueries(one);
        int forMany = await CountListQueries(many);

        forOne.Should().BeGreaterThan(0, "the counter must actually see the commands");
        forMany.Should().Be(forOne, "readiness for the whole list is read in one statement, not one per appointment");
        (await Mine(many)).Should().HaveCount(8).And.OnlyContain(a => a.Readiness != null && a.Readiness.Required == 4);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private async Task<int> CountListQueries(Seeded s)
    {
        CommandCounter counter = new();
        CurrentTenantService tenant = new();
        tenant.SetTenant(s.StudioId);
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0)))
            .AddInterceptors(counter)
            .Options;
        await using AppDbContext db = new(options, tenant);
        UseClient(s);

        await new GetMyAppointmentsHandler(db, _user).Handle(new GetMyAppointmentsQuery(), default);

        return counter.Count;
    }

    private async Task<ClientAppointmentDetailResponse> Detail(Seeded s, Guid id)
    {
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        UseClient(s);
        return await new GetMyAppointmentHandler(db, _user, Substitute.For<IIdentityService>(), Substitute.For<INotificationPreferenceService>()).Handle(new GetMyAppointmentQuery(id), default);
    }

    private async Task<List<AppointmentResponse>> Mine(Seeded s)
    {
        await using AppDbContext db = fixture.CreateDbContext(s.StudioId);
        UseClient(s);
        return await new GetMyAppointmentsHandler(db, _user).Handle(new GetMyAppointmentsQuery(), default);
    }

    private void UseClient(Seeded s)
    {
        _user.Role.Returns("client");
        _user.UserId.Returns(s.UserId);
    }

    private sealed record Seeded(Guid StudioId, Guid UserId, Guid ClientId, List<Guid> AppointmentIds);

    private async Task<Seeded> Seed(int appointments, bool requirements = true)
    {
        Guid studioId = Guid.NewGuid();
        Guid userId = Guid.NewGuid();
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        ctx.Studios.Add(new Studio { Id = studioId, Name = "Ready", Slug = studioId.ToString("N")[..8], CountryCode = "PT", Currency = "EUR" });
        Client client = new() { StudioId = studioId, UserId = userId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid():N}@c.test" };
        Service service = new()
        {
            StudioId = studioId,
            Name = "Session",
            DurationMinutes = 120,
            IsActive = true,
            RequiresConsentForm = requirements,
            RequiresIntakeForm = requirements,
            RequiresReferenceImage = requirements,
            RequiresDepositPaid = requirements,
        };
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
                ServiceId = service.Id,
                Date = start,
                EndDate = start.AddHours(2),
                DurationMinutes = 120,
                Status = AppointmentStatus.Confirmed,
                DepositStatus = DepositStatus.Pending,
                DepositAmount = 50m,
            };
            ctx.Appointments.Add(appointment);
            ids.Add(appointment.Id);
        }
        await ctx.SaveChangesAsync();

        return new Seeded(studioId, userId, client.Id, ids);
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
