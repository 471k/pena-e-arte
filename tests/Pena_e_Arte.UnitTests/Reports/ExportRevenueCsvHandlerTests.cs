using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Common;
using Pena_e_Arte.Application.Reports.Queries;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reports;

public class ExportRevenueCsvHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public ExportRevenueCsvHandlerTests() => _tenant.StudioId.Returns(_studioId);

    private static readonly DateTimeOffset Now = new(2026, 11, 12, 10, 0, 0, TimeSpan.Zero);

    private ExportRevenueCsvHandler CreateSut() => new(_db, _tenant, new FixedTimeProvider(Now));

    /// <summary>The header row and the payment rows after it; the scope block above them is skipped.</summary>
    private static string[] Table(string csv)
    {
        string[] lines = csv.TrimStart('\uFEFF').Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r')).ToArray();
        int header = Array.FindIndex(lines, l => l.StartsWith(ExportRevenueCsvHandler.HeaderFirstColumn + ",", StringComparison.Ordinal));
        header.Should().BeGreaterThanOrEqualTo(0, "the file has a header row");
        return lines[header..];
    }

    [Fact]
    public async Task Handle_NoPayments_ReturnsHeaderRowOnly()
    {
        string csv = await CreateSut().Handle(new ExportRevenueCsvQuery(null, null), default);

        csv.Should().StartWith(CsvUtils.Bom, "the BOM stays first so a spreadsheet reads the file as UTF-8");
        string[] lines = Table(csv);
        lines.Should().ContainSingle();
        lines[0].Should().StartWith("Paid At,Client,Artist");
    }

    [Fact]
    public async Task Handle_PaidPayment_ProducesRowWithRetainedAmount()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid clientId = await SeedClient("Jane", "Doe");
        Guid apptId = await SeedAppointment(artistId, clientId);
        await SeedPayment(apptId, clientId, 100m, PaymentStatus.Paid, DateTime.UtcNow, refundedAmount: null);

        string csv = await CreateSut().Handle(new ExportRevenueCsvQuery(null, null), default);

        string[] lines = Table(csv);
        lines.Should().HaveCount(2);
        lines[1].Should().Contain("Jane Doe").And.Contain("Luna Artista").And.Contain("100.00");
    }

    [Fact]
    public async Task Handle_PartiallyRefundedPayment_ShowsRetainedAmountNotFullAmount()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid clientId = await SeedClient("Jane", "Doe");
        Guid apptId = await SeedAppointment(artistId, clientId);
        await SeedPayment(apptId, clientId, 100m, PaymentStatus.Refunded, DateTime.UtcNow, refundedAmount: 40m);

        string csv = await CreateSut().Handle(new ExportRevenueCsvQuery(null, null), default);

        string[] lines = Table(csv);
        lines[1].Should().Contain("100.00").And.Contain("60.00");
    }

    [Fact]
    public async Task Handle_NameContainingCommaAndQuote_IsEscapedCorrectly()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid clientId = await SeedClient("Jane, \"JJ\"", "Doe");
        Guid apptId = await SeedAppointment(artistId, clientId);
        await SeedPayment(apptId, clientId, 50m, PaymentStatus.Paid, DateTime.UtcNow, refundedAmount: null);

        string csv = await CreateSut().Handle(new ExportRevenueCsvQuery(null, null), default);

        csv.Should().Contain("\"Jane, \"\"JJ\"\" Doe\"");
    }

    [Fact]
    public async Task Handle_PendingPayment_ExcludedFromExport()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid clientId = await SeedClient("Jane", "Doe");
        Guid apptId = await SeedAppointment(artistId, clientId);
        await SeedPayment(apptId, clientId, 50m, PaymentStatus.Pending, null, refundedAmount: null);

        string csv = await CreateSut().Handle(new ExportRevenueCsvQuery(null, null), default);

        string[] lines = Table(csv);
        lines.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_FromToRange_FiltersByPaidAt()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid clientId = await SeedClient("Jane", "Doe");
        Guid appt1 = await SeedAppointment(artistId, clientId);
        Guid appt2 = await SeedAppointment(artistId, clientId);
        await SeedPayment(appt1, clientId, 50m, PaymentStatus.Paid, DateTime.UtcNow.AddMonths(-6), refundedAmount: null);
        await SeedPayment(appt2, clientId, 70m, PaymentStatus.Paid, DateTime.UtcNow, refundedAmount: null);

        string csv = await CreateSut().Handle(
            new ExportRevenueCsvQuery(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1)), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1))), default);

        string[] lines = Table(csv);
        lines.Should().HaveCount(2);
        lines[1].Should().Contain("70.00");
    }

    [Fact]
    public async Task Handle_JpyPayment_FormatsAmountsWithZeroDecimals()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid clientId = await SeedClient("Jane", "Doe");
        Guid apptId = await SeedAppointment(artistId, clientId);
        _db.Payments.Add(new Payment
        {
            StudioId = _studioId,
            AppointmentId = apptId,
            ClientId = clientId,
            Amount = 3000m,
            Currency = "JPY",
            Status = PaymentStatus.Paid,
            Method = ClientPaymentMethod.Card,
            PaidAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        string csv = await CreateSut().Handle(new ExportRevenueCsvQuery(null, null), default);

        string[] lines = Table(csv);
        lines[0].Should().Contain("Currency");
        lines[1].Should().Contain(",3000,3000,JPY,");
    }

    [Fact]
    public async Task Handle_TimesAreInTheStudiosTimezone_NotUtc()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "S", Slug = "s", OwnerEmail = "o@x.test", Timezone = "Europe/Paris" });
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid clientId = await SeedClient("Jane", "Doe");
        Guid apptId = await SeedAppointment(artistId, clientId);
        Appointment appt = _db.Appointments.Single(a => a.Id == apptId);
        appt.Date = new DateTime(2026, 10, 23, 14, 0, 0, DateTimeKind.Utc);              // 16:00 Paris (CEST)
        await _db.SaveChangesAsync();
        await SeedPayment(apptId, clientId, 50m, PaymentStatus.Paid,
            new DateTime(2026, 11, 5, 23, 30, 0, DateTimeKind.Utc), refundedAmount: null); // 00:30 on 6 Nov in Paris (CET)

        string csv = await CreateSut().Handle(new ExportRevenueCsvQuery(null, null), default);

        string row = Table(csv)[1];
        row.Should().StartWith("2026-11-06 00:30,").And.Contain(",2026-10-23 16:00,");
    }

    // ── E05b-1 (O08): studio-local periods and the scope block ─────────────────

    private async Task<(Guid ClientId, Guid ArtistId)> TiraneStudioWithPeople()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "S", Slug = "s", OwnerEmail = "o@x.test", Timezone = "Europe/Tirane" });
        await _db.SaveChangesAsync();
        return (await SeedClient("Jane", "Doe"), await SeedArtist("Luna", "Artista"));
    }

    [Fact]
    public async Task Handle_HalfAnHourAfterLocalMidnight_IsInTheNewMonth_NotTheOld()
    {
        (Guid clientId, Guid artistId) = await TiraneStudioWithPeople();
        // 31 Oct 23:30 UTC is 1 Nov 00:30 in Tirane (UTC+1 since 25 Oct). The old UTC filter put it in October.
        await SeedPayment(await SeedAppointment(artistId, clientId), clientId, 50m, PaymentStatus.Paid,
            new DateTime(2026, 10, 31, 23, 30, 0, DateTimeKind.Utc), refundedAmount: null);

        string[] october = Table(await CreateSut().Handle(new ExportRevenueCsvQuery(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), default));
        string[] november = Table(await CreateSut().Handle(new ExportRevenueCsvQuery(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)), default));

        october.Should().ContainSingle("only the header: the payment was made on 1 Nov, studio time");
        november.Should().HaveCount(2);
        november[1].Should().StartWith("2026-11-01 00:30,", "the printed date and the period now agree");
    }

    [Fact]
    public async Task Handle_HalfAnHourBeforeLocalMidnight_StaysInTheOldMonth()
    {
        (Guid clientId, Guid artistId) = await TiraneStudioWithPeople();
        // 31 Oct 22:30 UTC is 31 Oct 23:30 in Tirane.
        await SeedPayment(await SeedAppointment(artistId, clientId), clientId, 50m, PaymentStatus.Paid,
            new DateTime(2026, 10, 31, 22, 30, 0, DateTimeKind.Utc), refundedAmount: null);

        string[] october = Table(await CreateSut().Handle(new ExportRevenueCsvQuery(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)), default));
        string[] november = Table(await CreateSut().Handle(new ExportRevenueCsvQuery(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30)), default));

        october.Should().HaveCount(2);
        october[1].Should().StartWith("2026-10-31 23:30,");
        november.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_TheFileStatesThePeriodTimezoneAndFiltersItWasMadeWith()
    {
        await TiraneStudioWithPeople();

        string csv = await CreateSut().Handle(
            new ExportRevenueCsvQuery(new DateOnly(2026, 11, 1), new DateOnly(2026, 11, 30), Search: "jane", Status: "Paid"), default);

        csv.Should().StartWith(CsvUtils.Bom + "Revenue export,");
        csv.Should().Contain("Period (studio dates),\"2026-11-01 to 2026-11-30, both days included\"");
        csv.Should().Contain("Period (UTC),\"From 2026-10-31T23:00Z up to, not including, 2026-11-30T23:00Z\"");
        csv.Should().Contain("Timezone,Europe/Tirane (every date and time below is in this timezone)");
        csv.Should().Contain("Statuses,Paid");
        csv.Should().Contain("Search,jane");
        csv.Should().Contain("Exported at,2026-11-12 11:00", "the export time, in studio time");
        // A blank line separates the scope block from the table.
        csv.Should().MatchRegex("Exported at,[^\r\n]*\r?\n\r?\nPaid At,");
    }

    [Fact]
    public async Task Handle_NoFilters_SaysAllPaymentsAndBothStatuses()
    {
        await TiraneStudioWithPeople();

        string csv = await CreateSut().Handle(new ExportRevenueCsvQuery(null, null), default);

        csv.Should().Contain("Period (studio dates),All payments up to the export time");
        csv.Should().Contain("Statuses,\"Paid, Refunded\"");
        csv.Should().Contain("Search,None");
    }

    [Fact]
    public async Task Handle_SearchAndStatus_NarrowTheRowsLikeThePaymentsList()
    {
        (Guid jane, Guid luna) = await TiraneStudioWithPeople();
        Guid omar = await SeedClient("Omar", "Kola");
        DateTime paid = new(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);
        await SeedPayment(await SeedAppointment(luna, jane), jane, 10m, PaymentStatus.Paid, paid, refundedAmount: null);
        await SeedPayment(await SeedAppointment(luna, jane), jane, 20m, PaymentStatus.Refunded, paid, refundedAmount: 20m);
        await SeedPayment(await SeedAppointment(luna, omar), omar, 30m, PaymentStatus.Paid, paid, refundedAmount: null);

        string[] janePaid = Table(await CreateSut().Handle(new ExportRevenueCsvQuery(null, null, Search: "Jane", Status: "Paid"), default));
        string[] byArtist = Table(await CreateSut().Handle(new ExportRevenueCsvQuery(null, null, Search: "Luna Art"), default)); // exact case: InMemory compares ordinally; MySQL ignores case (integration test)

        janePaid.Should().HaveCount(2);
        janePaid[1].Should().Contain("Jane Doe").And.Contain(",10.00,");
        byArtist.Should().HaveCount(4, "the artist's name matches all three of their payments");
    }

    private async Task<Guid> SeedArtist(string firstName, string lastName)
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            FirstName = firstName,
            LastName = lastName,
            Email = $"{Guid.NewGuid():N}@test.com",
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist.Id;
    }

    private async Task<Guid> SeedClient(string firstName, string lastName)
    {
        Client client = new()
        {
            StudioId = _studioId,
            FirstName = firstName,
            LastName = lastName,
            Email = $"{Guid.NewGuid():N}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return client.Id;
    }

    private async Task<Guid> SeedAppointment(Guid artistId, Guid clientId)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = artistId,
            ClientId = clientId,
            Date = DateTime.UtcNow.AddDays(1),
            EndDate = DateTime.UtcNow.AddDays(1).AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Completed,
            DepositStatus = DepositStatus.Paid,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return appointment.Id;
    }

    private async Task SeedPayment(
        Guid appointmentId, Guid clientId, decimal amount, PaymentStatus status, DateTime? paidAt,
        decimal? refundedAmount)
    {
        _db.Payments.Add(new Payment
        {
            StudioId = _studioId,
            AppointmentId = appointmentId,
            ClientId = clientId,
            Amount = amount,
            Currency = "EUR",
            Status = status,
            Method = ClientPaymentMethod.Card,
            PaidAt = paidAt,
            RefundedAmount = refundedAmount,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
