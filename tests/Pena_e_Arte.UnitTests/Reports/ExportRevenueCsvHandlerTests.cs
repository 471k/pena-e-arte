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

    private ExportRevenueCsvHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_NoPayments_ReturnsHeaderRowOnly()
    {
        string csv = await CreateSut().Handle(new ExportRevenueCsvQuery(null, null), default);

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().ContainSingle();
        lines[0].Should().StartWith(CsvUtils.Bom + "Paid At,Client,Artist");
    }

    [Fact]
    public async Task Handle_PaidPayment_ProducesRowWithRetainedAmount()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid clientId = await SeedClient("Jane", "Doe");
        Guid apptId = await SeedAppointment(artistId, clientId);
        await SeedPayment(apptId, clientId, 100m, PaymentStatus.Paid, DateTime.UtcNow, refundedAmount: null);

        string csv = await CreateSut().Handle(new ExportRevenueCsvQuery(null, null), default);

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
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

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
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

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
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
            new ExportRevenueCsvQuery(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow.AddDays(1)), default);

        string[] lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines.Should().HaveCount(2);
        lines[1].Should().Contain("70.00");
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
            Status = status,
            Method = ClientPaymentMethod.Card,
            PaidAt = paidAt,
            RefundedAmount = refundedAmount,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
