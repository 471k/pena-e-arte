using System.Data.Common;
using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Pena_e_Arte.Application.Payments.Queries;
using Pena_e_Arte.Application.Reports.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

/// <summary>
/// E05b-1 (O05 + O08) against real MySQL: the owner's payment history is searched, filtered and keyset-paged
/// in SQL, newest first; the revenue export applies the same filters over studio-local dates; and neither
/// costs more statements for a bigger page.
/// </summary>
[Collection("Database")]
public class PaymentHistoryIntegrationTests(DatabaseFixture fixture)
{
    private const string Tirane = "Europe/Tirane";

    // ── Search ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Search_MatchesClientOrArtistFullName_IgnoringCase()
    {
        Studio s = await NewStudio();
        Guid jane = await s.Payment(client: ("Jane", "Doe"), artist: ("Luna", "Artista"));
        Guid omar = await s.Payment(client: ("Omar", "Kola"), artist: ("Rafa", "Costa"));

        (await List(s, new GetPaymentsQuery(Search: "jane d"))).Select(p => p.Id).Should().Equal(jane);
        (await List(s, new GetPaymentsQuery(Search: "LUNA art"))).Select(p => p.Id).Should().Equal(jane);
        (await List(s, new GetPaymentsQuery(Search: "costa"))).Select(p => p.Id).Should().Equal(omar);
        (await List(s, new GetPaymentsQuery(Search: "  kola  "))).Select(p => p.Id).Should().Equal(new[] { omar }, "the term is trimmed");
    }

    [Fact]
    public async Task Search_NeverMatchesTheProviderReference_ClientToken_OrCashNote()
    {
        Studio s = await NewStudio();
        await s.Payment(client: ("Jane", "Doe"), artist: ("Luna", "Artista"),
            providerReference: "pi_zebra123", clientToken: "zebra", cashNote: "zebra");

        (await List(s, new GetPaymentsQuery(Search: "zebra"))).Should().BeEmpty();
        (await List(s, new GetPaymentsQuery(Search: "pi_"))).Should().BeEmpty();
    }

    [Fact]
    public async Task Search_StaysInsideTheCallersStudio()
    {
        Studio mine = await NewStudio();
        Studio theirs = await NewStudio();
        Guid my = await mine.Payment(client: ("Jane", "Doe"), artist: ("Luna", "Artista"));
        await theirs.Payment(client: ("Jane", "Doe"), artist: ("Luna", "Artista"));

        (await List(mine, new GetPaymentsQuery(Search: "jane"))).Select(p => p.Id).Should().Equal(my);
    }

    // ── Filters compose ──────────────────────────────────────────────────────

    [Fact]
    public async Task SearchStatusAndDates_Compose()
    {
        Studio s = await NewStudio();
        DateTime nov5 = Utc(2026, 11, 5, 12);
        Guid hit = await s.Payment(("Jane", "Doe"), ("Luna", "Artista"), PaymentStatus.Paid, paidAt: nov5);
        await s.Payment(("Jane", "Doe"), ("Luna", "Artista"), PaymentStatus.Refunded, paidAt: nov5, refunded: 50m); // wrong status
        await s.Payment(("Jane", "Doe"), ("Luna", "Artista"), PaymentStatus.Paid, paidAt: Utc(2026, 10, 5, 12));    // wrong month
        await s.Payment(("Omar", "Kola"), ("Rafa", "Costa"), PaymentStatus.Paid, paidAt: nov5);                     // wrong name
        await s.Payment(("Jane", "Doe"), ("Luna", "Artista"), PaymentStatus.Pending);                               // no PaidAt

        List<PaymentResponse> result = await List(s,
            new GetPaymentsQuery(Search: "jane", Status: "Paid", From: new DateOnly(2026, 11, 1), To: new DateOnly(2026, 11, 30)));

        result.Select(p => p.Id).Should().Equal(hit);
    }

    [Fact]
    public async Task DateFilter_LeavesOutUnpaidRows_AndUsesStudioLocalMidnight()
    {
        Studio s = await NewStudio();
        // 31 Oct 23:30 UTC is 1 Nov 00:30 in Tirane; 31 Oct 22:30 UTC is still 31 Oct there.
        Guid firstOfNov = await s.Payment(("A", "A"), ("B", "B"), PaymentStatus.Paid, paidAt: new DateTime(2026, 10, 31, 23, 30, 0, DateTimeKind.Utc));
        Guid lastOfOct = await s.Payment(("A", "A"), ("B", "B"), PaymentStatus.Paid, paidAt: new DateTime(2026, 10, 31, 22, 30, 0, DateTimeKind.Utc));
        await s.Payment(("A", "A"), ("B", "B"), PaymentStatus.Pending);

        (await List(s, new GetPaymentsQuery(From: new DateOnly(2026, 11, 1)))).Select(p => p.Id).Should().Equal(firstOfNov);
        (await List(s, new GetPaymentsQuery(To: new DateOnly(2026, 10, 31)))).Select(p => p.Id).Should().Equal(lastOfOct);
        (await List(s, new GetPaymentsQuery())).Should().HaveCount(3, "with no dates, an unpaid row is listed too");
    }

    // ── Keyset paging under the newest-first sort ────────────────────────────

    [Fact]
    public async Task KeysetPages_ReturnEveryRowExactlyOnce_NewestFirst_AcrossTiesAndUnpaidRows()
    {
        Studio s = await NewStudio();
        DateTime t0 = Utc(2026, 11, 1, 12);
        List<(Guid Id, DateTime Key)> seeded = [];
        for (int i = 0; i < 23; i++)
        {
            // Every third row shares a paid time with the next (a tie broken by Id), every fifth is unpaid
            // and sorts by when it was created.
            DateTime created = t0.AddHours(-i * 3 - 1);
            DateTime? paid = i % 5 == 4 ? null : t0.AddHours(-(i - i % 3 / 2) * 3);
            Guid id = await s.Payment(("C" + i, "X"), ("A", "B"), paid is null ? PaymentStatus.Pending : PaymentStatus.Paid,
                paidAt: paid, createdAt: created);
            seeded.Add((id, paid ?? created));
        }

        List<Guid> seen = [];
        Guid? cursor = null;
        int pages = 0;
        while (pages++ < 20)
        {
            List<PaymentResponse> page = await List(s, new GetPaymentsQuery(cursor, PageSize: 5));
            if (page.Count == 0) break;
            seen.AddRange(page.Select(p => p.Id));
            cursor = page[^1].Id;
        }

        pages.Should().BeGreaterThan(4, "23 rows at 5 a page is five pages, then an empty one");
        seen.Should().OnlyHaveUniqueItems();
        // MySQL orders a char(36) Guid as its lowercase text.
        List<Guid> expected = seeded
            .OrderByDescending(r => r.Key)
            .ThenByDescending(r => r.Id.ToString(), StringComparer.Ordinal)
            .Select(r => r.Id).ToList();
        seen.Should().Equal(expected);
    }

    [Fact]
    public async Task KeysetPages_WorkUnderAFilterToo()
    {
        Studio s = await NewStudio();
        HashSet<Guid> paid = [];
        for (int i = 0; i < 14; i++)
        {
            Guid id = await s.Payment(("C" + i, "X"), ("A", "B"), i % 2 == 0 ? PaymentStatus.Paid : PaymentStatus.Failed,
                paidAt: Utc(2026, 11, 1, 0).AddHours(i));
            if (i % 2 == 0) paid.Add(id);
        }

        List<Guid> seen = [];
        Guid? cursor = null;
        for (int page = 0; page < 10; page++)
        {
            List<PaymentResponse> rows = await List(s, new GetPaymentsQuery(cursor, PageSize: 3, Status: "Paid"));
            if (rows.Count == 0) break;
            seen.AddRange(rows.Select(r => r.Id));
            cursor = rows[^1].Id;
        }

        seen.Should().OnlyHaveUniqueItems().And.BeEquivalentTo(paid);
    }

    // ── Cost ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QueryCount_IsPinned_AndAPageOfTwentyCostsTheSameAsAPageOfOne()
    {
        Studio s = await NewStudio();
        for (int i = 0; i < 25; i++)
            await s.Payment(("C" + i, "X"), ("A", "B"), PaymentStatus.Paid, paidAt: Utc(2026, 11, 1, 0).AddMinutes(i));

        (int one, List<PaymentResponse> firstOne) = await Counted(s, new GetPaymentsQuery(PageSize: 1));
        (int twenty, List<PaymentResponse> firstTwenty) = await Counted(s, new GetPaymentsQuery(PageSize: 20));
        (int filtered, _) = await Counted(s, new GetPaymentsQuery(firstTwenty[^1].Id, 20, "c", "Paid",
            new DateOnly(2026, 10, 1), new DateOnly(2026, 11, 30)));

        // Measured 12 Nov 2026: one statement for a page (payments joined to client and appointment). A cursor
        // adds its lookup and a date filter adds the studio's timezone: three at most.
        one.Should().Be(1);
        twenty.Should().Be(one, "the page is one joined read, never a query per row");
        filtered.Should().Be(3);
        firstOne.Should().ContainSingle();
        firstTwenty.Should().HaveCount(20);
    }

    // ── The export reproduces the list ───────────────────────────────────────

    [Fact]
    public async Task Export_WithTheListsFilters_HoldsTheSamePaymentsTheListShows()
    {
        Studio s = await NewStudio();
        DateTime nov = Utc(2026, 11, 3, 9);
        await s.Payment(("Jane", "Doe"), ("Luna", "Artista"), PaymentStatus.Paid, paidAt: nov, amount: 40m);
        await s.Payment(("Jane", "Doe"), ("Luna", "Artista"), PaymentStatus.Refunded, paidAt: nov.AddHours(1), amount: 60m, refunded: 60m);
        await s.Payment(("Jane", "Doe"), ("Luna", "Artista"), PaymentStatus.Paid, paidAt: new DateTime(2026, 10, 31, 23, 30, 0, DateTimeKind.Utc), amount: 70m);
        await s.Payment(("Omar", "Kola"), ("Rafa", "Costa"), PaymentStatus.Paid, paidAt: nov, amount: 80m);
        await s.Payment(("Jane", "Doe"), ("Luna", "Artista"), PaymentStatus.Pending, amount: 90m);

        DateOnly from = new(2026, 11, 1), to = new(2026, 11, 30);
        List<PaymentResponse> list = await List(s, new GetPaymentsQuery(PageSize: 100, Search: "jane", From: from, To: to));
        string csv;
        await using (AppDbContext db = fixture.CreateDbContext(s.Id))
            csv = await new ExportRevenueCsvHandler(db, Tenant(s.Id), TimeProvider.System)
                .Handle(new ExportRevenueCsvQuery(from, to, Search: "jane"), default);

        string[] rows = csv.Split('\n').Select(l => l.TrimEnd('\r')).SkipWhile(l => !l.StartsWith("Paid At,", StringComparison.Ordinal))
            .Skip(1).Where(l => l.Length > 0).ToArray();
        static string Amount(decimal a) => a.ToString("F2", CultureInfo.InvariantCulture);

        list.Select(p => p.Amount).Should().BeEquivalentTo([40m, 60m, 70m], "1 Nov 00:30 local is in November");
        rows.Select(r => r.Split(',')[4]).Should().BeEquivalentTo(list.Select(p => Amount(p.Amount)));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static DateTime Utc(int y, int m, int d, int h) => new(y, m, d, h, 0, 0, DateTimeKind.Utc);

    private static CurrentTenantService Tenant(Guid studioId)
    {
        CurrentTenantService tenant = new();
        tenant.SetTenant(studioId);
        return tenant;
    }

    private async Task<List<PaymentResponse>> List(Studio s, GetPaymentsQuery q)
    {
        await using AppDbContext db = fixture.CreateDbContext(s.Id);
        return await new GetPaymentsHandler(db, Tenant(s.Id)).Handle(q, default);
    }

    private async Task<(int Count, List<PaymentResponse> Result)> Counted(Studio s, GetPaymentsQuery q)
    {
        CommandCounter counter = new();
        DbContextOptions<AppDbContext> options = new DbContextOptionsBuilder<AppDbContext>()
            .UseMySql(fixture.ConnectionString, new MySqlServerVersion(new Version(8, 0, 0)))
            .AddInterceptors(counter)
            .Options;
        CurrentTenantService tenant = Tenant(s.Id);
        await using AppDbContext db = new(options, tenant);
        List<PaymentResponse> result = await new GetPaymentsHandler(db, tenant).Handle(q, default);
        return (counter.Count, result);
    }

    private async Task<Studio> NewStudio()
    {
        Guid id = Guid.NewGuid();
        await using AppDbContext ctx = fixture.CreateDbContext(id);
        ctx.Studios.Add(new Pena_e_Arte.Domain.Entities.Studio
        {
            Id = id,
            Name = "History",
            Slug = id.ToString("N")[..8],
            CountryCode = "AL",
            Currency = "ALL",
            Timezone = Tirane,
        });
        await ctx.SaveChangesAsync();
        return new Studio(id, fixture);
    }

    private sealed class Studio(Guid id, DatabaseFixture fixture)
    {
        public Guid Id { get; } = id;

        public async Task<Guid> Payment(
            (string First, string Last) client, (string First, string Last) artist,
            PaymentStatus status = PaymentStatus.Paid, DateTime? paidAt = null, decimal amount = 50m,
            decimal? refunded = null, DateTime? createdAt = null,
            string? providerReference = null, string? clientToken = null, string? cashNote = null)
        {
            await using AppDbContext ctx = fixture.CreateDbContext(Id);
            Client c = new() { StudioId = Id, FirstName = client.First, LastName = client.Last, Email = $"{Guid.NewGuid():N}@c.test" };
            Artist a = new() { StudioId = Id, FirstName = artist.First, LastName = artist.Last, Email = $"{Guid.NewGuid():N}@a.test", IsActive = true };
            DateTime start = Utc(2026, 12, 1, 10);
            Appointment appt = new()
            {
                StudioId = Id,
                ClientId = c.Id,
                ArtistId = a.Id,
                Date = start,
                EndDate = start.AddHours(1),
                DurationMinutes = 60,
                Status = AppointmentStatus.Confirmed,
                DepositStatus = DepositStatus.Paid,
            };
            Payment p = new()
            {
                StudioId = Id,
                AppointmentId = appt.Id,
                ClientId = c.Id,
                Amount = amount,
                Currency = "ALL",
                Status = status,
                Method = ClientPaymentMethod.Card,
                Provider = "POK",
                PaidAt = paidAt,
                RefundedAmount = refunded,
                ProviderReferenceId = providerReference,
                ClientToken = clientToken,
                CashNote = cashNote,
                CreatedAt = createdAt ?? DateTime.UtcNow,
            };
            ctx.Clients.Add(c);
            ctx.Artists.Add(a);
            ctx.Appointments.Add(appt);
            ctx.Payments.Add(p);
            await ctx.SaveChangesAsync();
            return p.Id;
        }
    }

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count;

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
