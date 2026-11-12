using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// E05b-2 (O06) against real MySQL: the owner's unsettled-payments view costs a fixed number of statements
/// however many rows there are, puts each row in exactly one group from what the reconciliation job recorded,
/// and never shows another studio's payments.
/// </summary>
[Collection("Database")]
public class UnsettledPaymentsIntegrationTests(DatabaseFixture fixture)
{
    [Fact]
    public async Task TheQueryCount_DoesNotScaleWithTheNumberOfRows()
    {
        Seeded few = await Seed(perGroup: 1);
        Seeded many = await Seed(perGroup: 12);

        (int forFew, UnsettledPaymentsResponse fewResult) = await Run(few.StudioId);
        (int forMany, UnsettledPaymentsResponse manyResult) = await Run(many.StudioId);

        // A count and one bounded read for each of the three groups.
        forFew.Should().Be(6, "the cost is pinned so a new per-row query shows up here");
        forMany.Should().Be(forFew, "every group is a count plus one bounded read, never a query per row");

        fewResult.Groups.Select(g => g.Kind).Should().Equal(UnsettledPaymentKinds.All);
        fewResult.Groups.Should().OnlyContain(g => g.Total == 1);
        manyResult.Groups.Should().OnlyContain(g => g.Total == 12 && g.Items.Count == GetUnsettledPaymentsHandler.MaxItemsPerGroup);
    }

    [Fact]
    public async Task EachRowLandsInTheGroupTheJobsRecordedAnswerPutsItIn()
    {
        Seeded s = await Seed(perGroup: 1);

        (_, UnsettledPaymentsResponse result) = await Run(s.StudioId);

        UnsettledPaymentItemResponse disagrees = Single(result, UnsettledPaymentKinds.ProviderDisagrees);
        disagrees.PaymentId.Should().Be(s.Disagrees[0]);
        disagrees.ProviderStatusAtCheck.Should().Be("Refunded");
        disagrees.Status.Should().Be("Pending");

        UnsettledPaymentItemResponse held = Single(result, UnsettledPaymentKinds.OnHold);
        held.PaymentId.Should().Be(s.OnHold[0]);
        held.ReleaseDeadline.Should().BeNull("the job never releases a hold on a live appointment");
        held.CheckOverdue.Should().BeTrue("the provider last answered 30 hours ago, past the 26-hour limit");

        UnsettledPaymentItemResponse awaiting = Single(result, UnsettledPaymentKinds.AwaitingCardPayment);
        awaiting.PaymentId.Should().Be(s.Awaiting[0]);
        awaiting.ProviderCheckedAt.Should().BeNull("not yet asked");
        awaiting.CheckOverdue.Should().BeFalse("just created, so not asked yet is normal");
        awaiting.ReleaseDeadline.Should().BeCloseTo(DateTime.UtcNow.AddHours(23), TimeSpan.FromMinutes(1));
        awaiting.ClientName.Should().Be("C D");
    }

    [Fact]
    public async Task SettledCashAndUncheckedNothing_AppearsAsNoGroups()
    {
        Guid studioId = await Studio();
        await using (AppDbContext ctx = fixture.CreateDbContext(studioId))
        {
            (Guid clientId, Guid appointmentId) = await ClientAndAppointment(ctx, studioId);
            ctx.Payments.Add(Pay(studioId, clientId, appointmentId, PaymentStatus.CashPending, ClientPaymentMethod.Cash, providerRef: null));
            (Guid _, Guid paidAppt) = await ClientAndAppointment(ctx, studioId);
            ctx.Payments.Add(Pay(studioId, clientId, paidAppt, PaymentStatus.Paid, ClientPaymentMethod.Card, "pi_paid"));
            await ctx.SaveChangesAsync();
        }

        (int count, UnsettledPaymentsResponse result) = await Run(studioId);

        result.Groups.Should().BeEmpty("cash belongs to the dashboard's CashToConfirm group, and paid is settled");
        count.Should().Be(3, "an empty group costs its count only");
    }

    [Fact]
    public async Task OnlyTheCallersStudio_IsEverShown()
    {
        Seeded mine = await Seed(perGroup: 2);
        await Seed(perGroup: 3); // another studio's unsettled payments

        (_, UnsettledPaymentsResponse result) = await Run(mine.StudioId);

        result.Groups.Should().OnlyContain(g => g.Total == 2);
        result.Groups.SelectMany(g => g.Items).Select(i => i.PaymentId)
            .Should().OnlyContain(id => mine.All.Contains(id));
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static UnsettledPaymentItemResponse Single(UnsettledPaymentsResponse r, string kind) =>
        r.Groups.Single(g => g.Kind == kind).Items.Single();

    private async Task<(int Count, UnsettledPaymentsResponse Result)> Run(Guid studioId)
    {
        CommandCounter counter = new();
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0)))
            .AddInterceptors(counter)
            .Options;
        CurrentTenantService tenant = new();
        tenant.SetTenant(studioId);
        await using AppDbContext db = new(options, tenant);

        UnsettledPaymentsResponse result = await new GetUnsettledPaymentsHandler(db, TimeProvider.System)
            .Handle(new GetUnsettledPaymentsQuery(), default);
        return (counter.Count, result);
    }

    private sealed record Seeded(Guid StudioId, List<Guid> Disagrees, List<Guid> OnHold, List<Guid> Awaiting)
    {
        public HashSet<Guid> All => [.. Disagrees, .. OnHold, .. Awaiting];
    }

    private async Task<Guid> Studio()
    {
        Guid studioId = Guid.NewGuid();
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        ctx.Studios.Add(new Studio { Id = studioId, Name = "Money", Slug = studioId.ToString("N")[..8], CountryCode = "AL", Currency = "ALL" });
        await ctx.SaveChangesAsync();
        return studioId;
    }

    private static async Task<(Guid ClientId, Guid AppointmentId)> ClientAndAppointment(AppDbContext ctx, Guid studioId)
    {
        Client client = new() { StudioId = studioId, FirstName = "C", LastName = "D", Email = $"{Guid.NewGuid():N}@c.test" };
        ctx.Clients.Add(client);
        DateTime start = DateTime.UtcNow.AddDays(5);
        Appointment a = new()
        {
            StudioId = studioId,
            ClientId = client.Id,
            Date = start,
            EndDate = start.AddHours(2),
            DurationMinutes = 120,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Pending,
            DepositAmount = 50m,
        };
        ctx.Appointments.Add(a);
        await ctx.SaveChangesAsync();
        return (client.Id, a.Id);
    }

    private static Payment Pay(Guid studioId, Guid clientId, Guid appointmentId, PaymentStatus status,
        ClientPaymentMethod method, string? providerRef) =>
        new()
        {
            StudioId = studioId,
            ClientId = clientId,
            AppointmentId = appointmentId,
            Amount = 50m,
            Status = status,
            Method = method,
            ProviderReferenceId = providerRef,
            Currency = "ALL",
            Provider = providerRef is null ? string.Empty : "pok",
        };

    /// <summary><paramref name="perGroup"/> payments in every one of the three groups.</summary>
    private async Task<Seeded> Seed(int perGroup)
    {
        Guid studioId = await Studio();
        await using AppDbContext ctx = fixture.CreateDbContext(studioId);
        Seeded seeded = new(studioId, [], [], []);
        DateTime now = DateTime.UtcNow;

        for (int i = 0; i < perGroup; i++)
        {
            // Unpaid, and the provider's last answer said refunded: the job leaves it for a person.
            (Guid c1, Guid a1) = await ClientAndAppointment(ctx, studioId);
            Payment disagrees = Pay(studioId, c1, a1, PaymentStatus.Pending, ClientPaymentMethod.Card, $"pi_d_{Guid.NewGuid():N}");
            disagrees.ProviderCheckedAt = now.AddHours(-2);
            disagrees.ProviderStatusAtCheck = PaymentProviderStatus.Refunded;
            disagrees.HoldExpiresAt = now.AddHours(10);

            // Held, provider still says authorised, last answered 30 h ago.
            (Guid c2, Guid a2) = await ClientAndAppointment(ctx, studioId);
            Payment held = Pay(studioId, c2, a2, PaymentStatus.Captured, ClientPaymentMethod.Card, $"pi_h_{Guid.NewGuid():N}");
            held.ProviderCheckedAt = now.AddHours(-30);
            held.ProviderStatusAtCheck = PaymentProviderStatus.Authorized;

            // Unpaid, never asked yet, just created, expiring in 23 h (an order minted an hour ago).
            (Guid c3, Guid a3) = await ClientAndAppointment(ctx, studioId);
            Payment awaiting = Pay(studioId, c3, a3, PaymentStatus.Pending, ClientPaymentMethod.Card, $"pi_a_{Guid.NewGuid():N}");
            awaiting.HoldExpiresAt = now.AddHours(23);

            ctx.Payments.AddRange(disagrees, held, awaiting);
            seeded.Disagrees.Add(disagrees.Id);
            seeded.OnHold.Add(held.Id);
            seeded.Awaiting.Add(awaiting.Id);
        }
        await ctx.SaveChangesAsync();
        return seeded;
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
