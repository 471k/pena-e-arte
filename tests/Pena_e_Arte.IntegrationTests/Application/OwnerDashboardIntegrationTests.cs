using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// E05a against real MySQL: the owner's exceptions cost a fixed number of SQL statements however many
/// exceptions there are (no N+1, no query per group item), they stay inside the caller's studio, and
/// setup readiness reads its facts in one statement with the tenant filters applied.
/// </summary>
[Collection("Database")]
public class OwnerDashboardIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task TheExceptionsQueryCount_DoesNotScaleWithTheNumberOfExceptions()
    {
        Seeded few = await Seed(perGroup: 1);
        Seeded many = await Seed(perGroup: 12);

        (int forFew, OwnerExceptionsResponse fewResult) = await CountExceptionQueries(few);
        (int forMany, OwnerExceptionsResponse manyResult) = await CountExceptionQueries(many);

        // Measured 10 Nov 2026: 10 statements (count + items for unassigned, awaiting and cash = 6; the
        // window rows; the readiness facts; the shown unpaid deposits' payments; the studio currency).
        forFew.Should().Be(10, "the cost is pinned so a new per-item query shows up here");
        forMany.Should().Be(forFew, "every group is a count plus one bounded read, never a query per item");

        fewResult.Groups.Select(g => g.Kind).Should().BeEquivalentTo(OwnerExceptionKinds.All);
        fewResult.Groups.Should().OnlyContain(g => g.Total == 1);
        manyResult.Groups.Should().OnlyContain(g => g.Total == 12 && g.Items.Count == 10);
    }

    [Fact]
    public async Task Exceptions_StayInsideTheCallersStudio()
    {
        Seeded mine = await Seed(perGroup: 2);
        await Seed(perGroup: 3); // another studio's exceptions

        (_, OwnerExceptionsResponse result) = await CountExceptionQueries(mine);

        result.Groups.Should().OnlyContain(g => g.Total == 2);
        result.Groups.SelectMany(g => g.Items).Select(i => i.AppointmentId)
            .Should().OnlyContain(id => mine.AppointmentIds.Contains(id));
    }

    [Fact]
    public async Task SetupReadiness_SeededHoursAreUnreviewed_AndOtherStudiosRowsDontCount()
    {
        Guid bare = await BareStudio(withHours: true);
        await Seed(perGroup: 1); // a studio that has artists and a deposit rule

        await using AppDbContext db = fixture.CreateDbContext(bare);
        SetupReadinessResponse r = await new GetSetupReadinessHandler(db, Tenant(bare)).Handle(new GetSetupReadinessQuery(), default);

        r.Areas.Select(a => (a.Area, a.State)).Should().Equal(
            ("Artists", "NotConfigured"), ("DepositRules", "NotConfigured"), ("Hours", "DefaultUnreviewed"),
            ("PayModel", "DefaultUnreviewed"));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static CurrentTenantService Tenant(Guid studioId)
    {
        CurrentTenantService tenant = new();
        tenant.SetTenant(studioId);
        return tenant;
    }

    private async Task<(int Count, OwnerExceptionsResponse Result)> CountExceptionQueries(Seeded s)
    {
        CommandCounter counter = new();
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0)))
            .AddInterceptors(counter)
            .Options;
        CurrentTenantService tenant = Tenant(s.StudioId);
        await using AppDbContext db = new(options, tenant);

        OwnerExceptionsResponse result = await new GetOwnerExceptionsHandler(db, tenant, TimeProvider.System)
            .Handle(new GetOwnerExceptionsQuery(), default);
        return (counter.Count, result);
    }

    private sealed record Seeded(Guid StudioId, HashSet<Guid> AppointmentIds);

    private async Task<Guid> BareStudio(bool withHours)
    {
        Guid studioId = Guid.NewGuid();
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        ctx.Studios.Add(new Studio { Id = studioId, Name = "Bare", Slug = studioId.ToString("N")[..8], CountryCode = "AL", Currency = "ALL" });
        if (withHours)
            ctx.StudioHours.Add(new StudioHours { StudioId = studioId, DayOfWeek = DayOfWeek.Monday, StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(18, 0, 0) });
        await ctx.SaveChangesAsync();
        return studioId;
    }

    /// <summary><paramref name="perGroup"/> items in every one of the five groups.</summary>
    private async Task<Seeded> Seed(int perGroup)
    {
        Guid studioId = Guid.NewGuid();
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        ctx.Studios.Add(new Studio { Id = studioId, Name = "Busy", Slug = studioId.ToString("N")[..8], CountryCode = "PT", Currency = "EUR" });
        Artist artist = new() { StudioId = studioId, FirstName = "A", LastName = "B", Email = $"{Guid.NewGuid():N}@a.test", IsActive = true };
        Client client = new() { StudioId = studioId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid():N}@c.test" };
        Service needsConsent = new() { StudioId = studioId, Name = "Session", DurationMinutes = 120, IsActive = true, RequiresConsentForm = true };
        ctx.Artists.Add(artist);
        ctx.Clients.Add(client);
        ctx.Services.Add(needsConsent);
        ctx.DepositRules.Add(new DepositRule { StudioId = studioId, Name = "Std", AmountFixed = 50m, IsActive = true });
        await ctx.SaveChangesAsync();

        HashSet<Guid> ids = [];
        int minute = 0;
        Appointment Add(int daysOut, Guid? artistId, AppointmentStatus status, DepositStatus deposit, Guid? serviceId = null)
        {
            DateTime start = DateTime.UtcNow.AddDays(daysOut).AddMinutes(++minute * 3);
            Appointment a = new()
            {
                StudioId = studioId,
                ClientId = client.Id,
                ArtistId = artistId,
                ServiceId = serviceId,
                Date = start,
                EndDate = start.AddHours(2),
                DurationMinutes = 120,
                Status = status,
                DepositStatus = deposit,
                DepositAmount = 50m,
            };
            ctx.Appointments.Add(a);
            ids.Add(a.Id);
            return a;
        }

        for (int i = 0; i < perGroup; i++)
        {
            Add(20, null, AppointmentStatus.Pending, DepositStatus.Paid);                 // Unassigned
            Add(20, artist.Id, AppointmentStatus.Pending, DepositStatus.Paid);            // AwaitingConfirmation
            Appointment cash = Add(20, artist.Id, AppointmentStatus.Confirmed, DepositStatus.Pending); // CashToConfirm
            ctx.Payments.Add(new Payment
            {
                StudioId = studioId,
                AppointmentId = cash.Id,
                ClientId = client.Id,
                Amount = 50m,
                Status = PaymentStatus.CashPending,
                Method = ClientPaymentMethod.Cash,
                Currency = "EUR",
            });
            Add(2, artist.Id, AppointmentStatus.Confirmed, DepositStatus.Pending);        // UnpaidDeposit
            Add(3, artist.Id, AppointmentStatus.Confirmed, DepositStatus.Paid, needsConsent.Id); // Preparation
        }
        await ctx.SaveChangesAsync();

        return new Seeded(studioId, ids);
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
    }
}
