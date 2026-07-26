using FluentAssertions;
using Pena_e_Arte.Application.Reports.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reports;

public class GetRevenueSummaryHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetRevenueSummaryHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoPayments_ReturnsEmptyTrendAndEmptyPerArtist()
    {
        RevenueSummaryResponse result = await CreateSut().Handle(new GetRevenueSummaryQuery(), default);

        result.MonthlyTrend.Should().HaveCount(12);
        result.MonthlyTrend.Should().OnlyContain(p => p.Revenue == 0m);
        result.PerArtist.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PaidPaymentThisMonth_ContributesToTrendAndPerArtist()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid apptId = await SeedAppointment(artistId);
        await SeedPayment(apptId, 50m, PaymentStatus.Paid, DateTime.UtcNow);

        RevenueSummaryResponse result = await CreateSut().Handle(new GetRevenueSummaryQuery(), default);

        result.MonthlyTrend.Last().Revenue.Should().Be(50m);
        result.PerArtist.Should().ContainSingle(a => a.ArtistId == artistId && a.Revenue == 50m
            && a.ArtistName == "Luna Artista");
    }

    [Fact]
    public async Task Handle_NonPaidPayment_ExcludedFromTrendAndPerArtist()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid apptId = await SeedAppointment(artistId);
        await SeedPayment(apptId, 50m, PaymentStatus.Pending, null);

        RevenueSummaryResponse result = await CreateSut().Handle(new GetRevenueSummaryQuery(), default);

        result.MonthlyTrend.Should().OnlyContain(p => p.Revenue == 0m);
        result.PerArtist.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PaymentOutsideSelectedPeriod_ExcludedFromPerArtistButCountsInTrend()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid apptId = await SeedAppointment(artistId);
        // Paid 6 months ago — still within the 12-month trend, but outside the
        // default 30-day per-artist period.
        DateTime paidAt = DateTime.UtcNow.AddMonths(-6);
        await SeedPayment(apptId, 80m, PaymentStatus.Paid, paidAt);

        RevenueSummaryResponse result = await CreateSut().Handle(new GetRevenueSummaryQuery(), default);

        result.MonthlyTrend.Sum(p => p.Revenue).Should().Be(80m);
        result.PerArtist.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MultipleArtists_SortedByRevenueDescending()
    {
        Guid highArtist = await SeedArtist("High", "Earner");
        Guid lowArtist = await SeedArtist("Low", "Earner");

        Guid apptHigh = await SeedAppointment(highArtist);
        Guid apptLow = await SeedAppointment(lowArtist);
        await SeedPayment(apptHigh, 200m, PaymentStatus.Paid, DateTime.UtcNow);
        await SeedPayment(apptLow, 30m, PaymentStatus.Paid, DateTime.UtcNow);

        RevenueSummaryResponse result = await CreateSut().Handle(new GetRevenueSummaryQuery(), default);

        result.PerArtist.Should().HaveCount(2);
        result.PerArtist[0].ArtistId.Should().Be(highArtist);
        result.PerArtist[1].ArtistId.Should().Be(lowArtist);
    }

    [Fact]
    public async Task Handle_SameArtistMultiplePayments_AggregatesRevenue()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid appt1 = await SeedAppointment(artistId);
        Guid appt2 = await SeedAppointment(artistId);
        await SeedPayment(appt1, 40m, PaymentStatus.Paid, DateTime.UtcNow);
        await SeedPayment(appt2, 60m, PaymentStatus.Paid, DateTime.UtcNow);

        RevenueSummaryResponse result = await CreateSut().Handle(new GetRevenueSummaryQuery(), default);

        result.PerArtist.Should().ContainSingle(a => a.ArtistId == artistId && a.Revenue == 100m);
    }

    [Fact]
    public async Task Handle_PartiallyRefundedPayment_CountsOnlyRetainedAmount()
    {
        // Regression: a late self-cancellation with a studio's partial-refund policy leaves
        // Status == Refunded (there's no PartiallyRefunded status) — the retained portion
        // must still show up as revenue, not disappear entirely.
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid apptId = await SeedAppointment(artistId);
        await SeedPayment(apptId, 100m, PaymentStatus.Refunded, DateTime.UtcNow, refundedAmount: 50m);

        RevenueSummaryResponse result = await CreateSut().Handle(new GetRevenueSummaryQuery(), default);

        result.MonthlyTrend.Last().Revenue.Should().Be(50m);
        result.PerArtist.Should().ContainSingle(a => a.ArtistId == artistId && a.Revenue == 50m);
    }

    [Fact]
    public async Task Handle_FullyRefundedPayment_ContributesZeroRevenue()
    {
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid apptId = await SeedAppointment(artistId);
        await SeedPayment(apptId, 100m, PaymentStatus.Refunded, DateTime.UtcNow, refundedAmount: 100m);

        RevenueSummaryResponse result = await CreateSut().Handle(new GetRevenueSummaryQuery(), default);

        result.MonthlyTrend.Should().OnlyContain(p => p.Revenue == 0m);
        result.PerArtist.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_RefundedPaymentWithNoRefundedAmountRecorded_CountsFullAmount()
    {
        // Defensive: a Refunded payment with RefundedAmount left null (shouldn't happen for
        // new cancellations, but guards old/pre-migration rows) must not double-count revenue
        // by treating null as "nothing refunded" rather than crashing or under-counting.
        Guid artistId = await SeedArtist("Luna", "Artista");
        Guid apptId = await SeedAppointment(artistId);
        await SeedPayment(apptId, 100m, PaymentStatus.Refunded, DateTime.UtcNow, refundedAmount: null);

        RevenueSummaryResponse result = await CreateSut().Handle(new GetRevenueSummaryQuery(), default);

        result.MonthlyTrend.Last().Revenue.Should().Be(100m);
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

    private async Task<Guid> SeedAppointment(Guid artistId)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = artistId,
            ClientId = Guid.NewGuid(),
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
        Guid appointmentId, decimal amount, PaymentStatus status, DateTime? paidAt,
        decimal? refundedAmount = null)
    {
        _db.Payments.Add(new Payment
        {
            StudioId = _studioId,
            AppointmentId = appointmentId,
            ClientId = Guid.NewGuid(),
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
