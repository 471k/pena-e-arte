using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Reports.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Reports;

public class GetMyEarningsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public GetMyEarningsHandlerTests()
    {
        _currentUser.UserId.Returns(_userId);
        _tenant.StudioId.Returns(_studioId);
        _db.Studios.Add(new Studio { Id = _studioId, Name = "Test", Slug = "test", CountryCode = "AL", Currency = "EUR" });
        _db.SaveChanges();
    }

    private GetMyEarningsHandler CreateSut() =>
        new(_db, _currentUser, _tenant, NullLogger<GetMyEarningsHandler>.Instance);

    // A07a phase 1: the live defect. An artist on 60% commission was shown the whole client payment
    // as their earnings. Red at 25c24715 (it returns 10,000).
    [Fact]
    public async Task MyEarnings_DoesNotReportStudioGrossAsArtistEarnings()
    {
        Guid artistId = await SeedArtistAsync(commissionRate: 60m);
        Guid apptId = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(-1));
        await SeedPayment(apptId, 10_000m, PaymentStatus.Paid, DateTime.UtcNow.AddHours(-1), "Client", "One");

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.PeriodTotal.Should().Be(6_000m);
    }

    [Fact]
    public async Task Handle_NoArtistProfileForCaller_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetMyEarningsQuery(), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_NoPayments_ReturnsEmptyTrendAndEmptyPayments()
    {
        await SeedArtistAsync();

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.MonthlyTrend.Should().HaveCount(12);
        result.MonthlyTrend.Should().OnlyContain(p => p.ClientPaid == 0m);
        result.Payments.Should().BeEmpty();
        result.ClientPaidTotal.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_PaidPaymentForOwnAppointment_ContributesToTrendAndPayments()
    {
        Guid artistId = await SeedArtistAsync();
        Guid apptId = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(1));
        await SeedPayment(apptId, 80m, PaymentStatus.Paid, DateTime.UtcNow, "Client", "One");

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.MonthlyTrend.Last().ClientPaid.Should().Be(80m);
        result.ClientPaidTotal.Should().Be(80m);
        result.Payments.Should().ContainSingle(p => p.ClientPaidAmount == 80m && p.ClientName == "Client One");
    }

    [Fact]
    public async Task Handle_PaymentForAnotherArtistsAppointment_Excluded()
    {
        await SeedArtistAsync();
        Guid otherArtistId = Guid.NewGuid();
        Guid apptId = await SeedAppointment(otherArtistId, DateTime.UtcNow.AddDays(1));
        await SeedPayment(apptId, 80m, PaymentStatus.Paid, DateTime.UtcNow, "Someone", "Else");

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.MonthlyTrend.Should().OnlyContain(p => p.ClientPaid == 0m);
        result.Payments.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NonPaidPayment_ExcludedFromTrendAndPayments()
    {
        Guid artistId = await SeedArtistAsync();
        Guid apptId = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(1));
        await SeedPayment(apptId, 80m, PaymentStatus.Pending, null, "Client", "One");

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.MonthlyTrend.Should().OnlyContain(p => p.ClientPaid == 0m);
        result.Payments.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PartiallyRefundedPayment_CountsOnlyRetainedAmount()
    {
        Guid artistId = await SeedArtistAsync();
        Guid apptId = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(1));
        await SeedPayment(apptId, 100m, PaymentStatus.Refunded, DateTime.UtcNow, "Client", "One", refundedAmount: 40m);

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.MonthlyTrend.Last().ClientPaid.Should().Be(60m);
        result.ClientPaidTotal.Should().Be(60m);
        result.Payments.Should().ContainSingle(p => p.ClientPaidAmount == 60m);
    }

    [Fact]
    public async Task Handle_PaymentOutsideSelectedPeriod_ExcludedFromPaymentsButCountsInTrend()
    {
        Guid artistId = await SeedArtistAsync();
        Guid apptId = await SeedAppointment(artistId, DateTime.UtcNow.AddMonths(-6));
        DateTime paidAt = DateTime.UtcNow.AddMonths(-6);
        await SeedPayment(apptId, 90m, PaymentStatus.Paid, paidAt, "Client", "One");

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.MonthlyTrend.Sum(p => p.ClientPaid).Should().Be(90m);
        result.Payments.Should().BeEmpty();
        result.ClientPaidTotal.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_PaymentWithSessionSplits_IncludesSplitsOnTheLine()
    {
        Guid artistId = await SeedArtistAsync();
        Guid apptId = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(1));
        Guid paymentId = await SeedPayment(apptId, 100m, PaymentStatus.Paid, DateTime.UtcNow, "Client", "One");
        _db.SessionSplits.Add(new SessionSplit
        {
            StudioId = _studioId,
            PaymentId = paymentId,
            Label = "Artist cut",
            Amount = 80m,
        });
        _db.SessionSplits.Add(new SessionSplit
        {
            StudioId = _studioId,
            PaymentId = paymentId,
            Label = "Studio fee",
            Amount = 20m,
        });
        await _db.SaveChangesAsync();

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        EarningsPaymentLine line = result.Payments.Should().ContainSingle().Subject;
        line.Splits.Should().HaveCount(2);
        line.Splits.Should().Contain(s => s.Label == "Artist cut" && s.Amount == 80m);
        line.Splits.Should().Contain(s => s.Label == "Studio fee" && s.Amount == 20m);
    }

    [Fact]
    public async Task Handle_PaymentInAnotherCurrency_ExcludedFromTrendAndPayments()
    {
        Guid artistId = await SeedArtistAsync();
        Guid apptId = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(1));
        Guid appt2Id = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(1));
        await SeedPayment(apptId, 80m, PaymentStatus.Paid, DateTime.UtcNow, "Client", "One");
        Client otherCurrencyClient = new()
        {
            StudioId = _studioId,
            FirstName = "All",
            LastName = "Payer",
            Email = $"{Guid.NewGuid():N}@test.com",
        };
        _db.Clients.Add(otherCurrencyClient);
        await _db.SaveChangesAsync();
        _db.Payments.Add(new Payment
        {
            StudioId = _studioId,
            AppointmentId = appt2Id,
            ClientId = otherCurrencyClient.Id,
            Amount = 5000m,
            Currency = "ALL",
            Status = PaymentStatus.Paid,
            Method = ClientPaymentMethod.Card,
            PaidAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.ClientPaidTotal.Should().Be(80m);
        result.Payments.Should().ContainSingle(p => p.ClientPaidAmount == 80m);
        result.Currency.Should().Be("EUR");
        result.ExcludedOtherCurrencyCount.Should().Be(1);
    }

    // ── A07a phase 5: the artist's share ────────────────────────────────────

    [Fact]
    public async Task UnsetRate_ShowsNoTotal_NeverZeroOrTheGross()
    {
        // Registration's default: Commission with no rate, and no rate of the artist's own.
        Guid artistId = await SeedArtistAsync();
        Guid apptId = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(-1));
        await SeedPayment(apptId, 10_000m, PaymentStatus.Paid, DateTime.UtcNow.AddHours(-1), "Client", "One");

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.ShareIsKnown.Should().BeFalse();
        result.PeriodTotal.Should().BeNull();
        result.ClientPaidTotal.Should().Be(10_000m, "what clients paid is still shown, as what it is");
        result.Payments.Should().ContainSingle(l => l.ArtistAmount == null && !l.ShareIsKnown && l.ClientPaidAmount == 10_000m);
        result.MonthlyTrend.Last().Should().Be(new EarningsMonthPoint(result.MonthlyTrend.Last().Month, null, 10_000m, false));
        result.MonthlyTrend.SkipLast(1).Should().OnlyContain(m => m.ShareIsKnown && m.ArtistAmount == 0m, "a month with no payments is a known zero");
    }

    [Fact]
    public async Task BoothRent_ArtistSeesTheFullAmount()
    {
        await SetStudioTerms(ArtistPayArrangement.BoothRent);
        Guid artistId = await SeedArtistAsync();
        Guid apptId = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(-1));
        await SeedPayment(apptId, 10_000m, PaymentStatus.Paid, DateTime.UtcNow.AddHours(-1), "Client", "One");

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.Should().Match<ArtistEarningsResponse>(r => r.ShareIsKnown && r.PeriodTotal == 10_000m && r.Arrangement == "BoothRent");
        result.MonthlyTrend.Last().ArtistAmount.Should().Be(10_000m);
    }

    [Fact]
    public async Task Salaried_IsAKnownZero_NotUnknown()
    {
        await SetStudioTerms(ArtistPayArrangement.Salaried);
        Guid artistId = await SeedArtistAsync();
        Guid apptId = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(-1));
        await SeedPayment(apptId, 10_000m, PaymentStatus.Paid, DateTime.UtcNow.AddHours(-1), "Client", "One");

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.ShareIsKnown.Should().BeTrue();
        result.PeriodTotal.Should().Be(0m);
        result.ClientPaidTotal.Should().Be(10_000m);
    }

    [Fact]
    public async Task AnArtistsOwnTerms_BeatTheStudios_AndTheResponseSaysWhich()
    {
        await SetStudioTerms(ArtistPayArrangement.Commission, rate: 50m);
        Guid artistId = await SeedArtistAsync(commissionRate: 70m);
        Guid apptId = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(-1));
        await SeedPayment(apptId, 1_000m, PaymentStatus.Paid, DateTime.UtcNow.AddHours(-1), "Client", "One", refundedAmount: 200m);

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.PeriodTotal.Should().Be(560m, "70% of the 800 kept after the refund");
        result.Arrangement.Should().Be("Commission");
        result.CommissionRate.Should().Be(70m);
        result.FlatFeePerSession.Should().BeNull();
    }

    [Fact]
    public async Task OtherCurrencyPayments_StayExcluded_FromTheShareToo()
    {
        await SetStudioTerms(ArtistPayArrangement.Commission, rate: 50m);
        Guid artistId = await SeedArtistAsync();
        Guid eurAppt = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(-1));
        Guid allAppt = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(-2));
        await SeedPayment(eurAppt, 80m, PaymentStatus.Paid, DateTime.UtcNow.AddHours(-1), "Client", "One");
        Guid allPayment = await SeedPayment(allAppt, 5_000m, PaymentStatus.Paid, DateTime.UtcNow.AddHours(-2), "Client", "Two");
        _db.Payments.Single(p => p.Id == allPayment).Currency = "ALL";
        await _db.SaveChangesAsync();

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.ExcludedOtherCurrencyCount.Should().Be(1);
        result.PeriodTotal.Should().Be(40m);
        result.ClientPaidTotal.Should().Be(80m);
    }

    private async Task SetStudioTerms(ArtistPayArrangement arrangement, decimal? rate = null, decimal? fee = null)
    {
        Studio studio = _db.Studios.Single(s => s.Id == _studioId);
        studio.DefaultPayArrangement = arrangement;
        studio.DefaultCommissionRate = rate;
        studio.DefaultFlatFeePerSession = fee;
        await _db.SaveChangesAsync();
    }

    private async Task<Guid> SeedArtistAsync(decimal? commissionRate = null)
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            UserId = _userId,
            FirstName = "Luna",
            LastName = "Artista",
            Email = $"{Guid.NewGuid():N}@test.com",
            CommissionRate = commissionRate,
        };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist.Id;
    }

    private async Task<Guid> SeedAppointment(Guid artistId, DateTime date)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = artistId,
            ClientId = Guid.NewGuid(),
            Date = date,
            EndDate = date.AddHours(1),
            DurationMinutes = 60,
            Status = AppointmentStatus.Completed,
            DepositStatus = DepositStatus.Paid,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        return appointment.Id;
    }

    private async Task<Guid> SeedPayment(
        Guid appointmentId, decimal amount, PaymentStatus status, DateTime? paidAt,
        string clientFirstName, string clientLastName, decimal? refundedAmount = null)
    {
        Client client = new()
        {
            StudioId = _studioId,
            FirstName = clientFirstName,
            LastName = clientLastName,
            Email = $"{Guid.NewGuid():N}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = appointmentId,
            ClientId = client.Id,
            Amount = amount,
            Currency = "EUR",
            Status = status,
            Method = ClientPaymentMethod.Card,
            PaidAt = paidAt,
            RefundedAmount = refundedAmount,
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return payment.Id;
    }
}
