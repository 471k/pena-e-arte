using FluentAssertions;
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
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public GetMyEarningsHandlerTests() => _currentUser.UserId.Returns(_userId);

    private GetMyEarningsHandler CreateSut() => new(_db, _currentUser);

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
        result.MonthlyTrend.Should().OnlyContain(p => p.Revenue == 0m);
        result.Payments.Should().BeEmpty();
        result.PeriodTotal.Should().Be(0m);
    }

    [Fact]
    public async Task Handle_PaidPaymentForOwnAppointment_ContributesToTrendAndPayments()
    {
        Guid artistId = await SeedArtistAsync();
        Guid apptId = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(1));
        await SeedPayment(apptId, 80m, PaymentStatus.Paid, DateTime.UtcNow, "Client", "One");

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.MonthlyTrend.Last().Revenue.Should().Be(80m);
        result.PeriodTotal.Should().Be(80m);
        result.Payments.Should().ContainSingle(p => p.Amount == 80m && p.ClientName == "Client One");
    }

    [Fact]
    public async Task Handle_PaymentForAnotherArtistsAppointment_Excluded()
    {
        await SeedArtistAsync();
        Guid otherArtistId = Guid.NewGuid();
        Guid apptId = await SeedAppointment(otherArtistId, DateTime.UtcNow.AddDays(1));
        await SeedPayment(apptId, 80m, PaymentStatus.Paid, DateTime.UtcNow, "Someone", "Else");

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.MonthlyTrend.Should().OnlyContain(p => p.Revenue == 0m);
        result.Payments.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_NonPaidPayment_ExcludedFromTrendAndPayments()
    {
        Guid artistId = await SeedArtistAsync();
        Guid apptId = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(1));
        await SeedPayment(apptId, 80m, PaymentStatus.Pending, null, "Client", "One");

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.MonthlyTrend.Should().OnlyContain(p => p.Revenue == 0m);
        result.Payments.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_PartiallyRefundedPayment_CountsOnlyRetainedAmount()
    {
        Guid artistId = await SeedArtistAsync();
        Guid apptId = await SeedAppointment(artistId, DateTime.UtcNow.AddDays(1));
        await SeedPayment(apptId, 100m, PaymentStatus.Refunded, DateTime.UtcNow, "Client", "One", refundedAmount: 40m);

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.MonthlyTrend.Last().Revenue.Should().Be(60m);
        result.PeriodTotal.Should().Be(60m);
        result.Payments.Should().ContainSingle(p => p.Amount == 60m);
    }

    [Fact]
    public async Task Handle_PaymentOutsideSelectedPeriod_ExcludedFromPaymentsButCountsInTrend()
    {
        Guid artistId = await SeedArtistAsync();
        Guid apptId = await SeedAppointment(artistId, DateTime.UtcNow.AddMonths(-6));
        DateTime paidAt = DateTime.UtcNow.AddMonths(-6);
        await SeedPayment(apptId, 90m, PaymentStatus.Paid, paidAt, "Client", "One");

        ArtistEarningsResponse result = await CreateSut().Handle(new GetMyEarningsQuery(), default);

        result.MonthlyTrend.Sum(p => p.Revenue).Should().Be(90m);
        result.Payments.Should().BeEmpty();
        result.PeriodTotal.Should().Be(0m);
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

    private async Task<Guid> SeedArtistAsync()
    {
        Artist artist = new()
        {
            StudioId = _studioId,
            UserId = _userId,
            FirstName = "Luna",
            LastName = "Artista",
            Email = $"{Guid.NewGuid():N}@test.com",
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
