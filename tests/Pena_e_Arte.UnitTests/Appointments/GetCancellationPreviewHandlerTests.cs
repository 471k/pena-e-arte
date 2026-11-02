using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class GetCancellationPreviewHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _studioId = Guid.NewGuid();

    public GetCancellationPreviewHandlerTests()
    {
        _db.Studios.Add(new Studio { Id = _studioId, Name = "S", Slug = "s", CountryCode = "AL", Currency = "ALL" });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private GetCancellationPreviewHandler CreateSut() => new(_db, _currentUser);

    private Task<CancellationPreviewResponse> Preview(Guid appointmentId) =>
        CreateSut().Handle(new GetCancellationPreviewQuery(appointmentId), default);

    [Fact]
    public async Task OwnClient_WithEnoughNotice_IsProjectedAFullRefund()
    {
        Guid clientId = SeedClientAsCurrentUser();
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddDays(7));
        await SeedPayment(id, PaymentStatus.Paid);

        CancellationPreviewResponse result = await Preview(id);

        result.AppointmentId.Should().Be(id);
        result.NoticeWindowHours.Should().Be(48);
        result.IsWithinNoticeWindow.Should().BeTrue();
        result.NoticeWindowApplies.Should().BeTrue();
        result.RefundPercent.Should().Be(100);
        result.DepositAmount.Should().Be(50m);
        result.Currency.Should().Be("EUR");
        result.RefundAmount.Should().Be(50m);
        result.KeptAmount.Should().Be(0m);
        result.Outcome.Should().Be(nameof(CancellationClientOutcome.FullyRefunded));
        result.IsProjection.Should().BeTrue();
    }

    [Fact]
    public async Task OwnClient_InsideTheWindow_FollowsTheStudiosRule()
    {
        Guid clientId = SeedClientAsCurrentUser();
        await SeedRule(windowHours: 72, lateRefundPercent: 25);
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddHours(24));
        await SeedPayment(id, PaymentStatus.Paid);

        CancellationPreviewResponse result = await Preview(id);

        result.NoticeWindowHours.Should().Be(72);
        result.IsWithinNoticeWindow.Should().BeFalse();
        result.RefundPercent.Should().Be(25);
        result.RefundAmount.Should().Be(12.5m);
        result.KeptAmount.Should().Be(37.5m);
        result.Outcome.Should().Be(nameof(CancellationClientOutcome.PartiallyRefunded));
    }

    [Fact]
    public async Task WrongClient_Is404_Not403()
    {
        SeedClientAsCurrentUser();
        Guid someoneElse = Guid.NewGuid();
        Guid id = await SeedAppointment(someoneElse, DateTime.UtcNow.AddDays(7));

        Func<Task> act = () => Preview(id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task ClientWithNoClientRecord_Is404()
    {
        _currentUser.Role.Returns("client");
        _currentUser.UserId.Returns(Guid.NewGuid());
        Guid id = await SeedAppointment(Guid.NewGuid(), DateTime.UtcNow.AddDays(7));

        Func<Task> act = () => Preview(id);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task MissingAppointment_Is404()
    {
        SeedClientAsCurrentUser();

        Func<Task> act = () => Preview(Guid.NewGuid());

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Theory]
    [InlineData("artist")]
    [InlineData("owner")]
    public async Task Staff_IsNotSubjectToTheNoticeWindow(string role)
    {
        _currentUser.Role.Returns(role);
        await SeedRule(windowHours: 72, lateRefundPercent: 0);
        Guid id = await SeedAppointment(Guid.NewGuid(), DateTime.UtcNow.AddHours(2));
        await SeedPayment(id, PaymentStatus.Captured);

        CancellationPreviewResponse result = await Preview(id);

        result.NoticeWindowApplies.Should().BeFalse();
        result.IsWithinNoticeWindow.Should().BeFalse("the window is still reported, it just doesn't apply");
        result.RefundPercent.Should().Be(100);
        result.RefundAmount.Should().Be(50m);
        result.Outcome.Should().Be(nameof(CancellationClientOutcome.FullyRefunded));
    }

    [Fact]
    public async Task NoPayment_UsesTheStudioCurrency_AndNothingIsCollected()
    {
        Guid clientId = SeedClientAsCurrentUser();
        Guid id = await SeedAppointment(clientId, DateTime.UtcNow.AddHours(2));

        CancellationPreviewResponse result = await Preview(id);

        result.Currency.Should().Be("ALL");
        result.RefundAmount.Should().Be(0m);
        result.KeptAmount.Should().Be(0m);
        result.Outcome.Should().Be(nameof(CancellationClientOutcome.NothingCollected));
    }

    private Guid SeedClientAsCurrentUser()
    {
        Guid userId = Guid.NewGuid();
        Client client = new() { StudioId = _studioId, UserId = userId, FirstName = "A", LastName = "B", Email = "a@b.com" };
        _db.Clients.Add(client);
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        _currentUser.Role.Returns("client");
        _currentUser.UserId.Returns(userId);
        return client.Id;
    }

    private async Task SeedRule(int windowHours, int lateRefundPercent)
    {
        _db.DepositRules.Add(new DepositRule
        {
            StudioId = _studioId,
            Name = "Rule",
            AmountFixed = 50m,
            IsActive = true,
            CancellationWindowHours = windowHours,
            RefundPercentOnLateCancel = lateRefundPercent,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private async Task<Guid> SeedAppointment(Guid clientId, DateTime date)
    {
        Appointment appointment = new()
        {
            StudioId = _studioId,
            ArtistId = Guid.NewGuid(),
            ClientId = clientId,
            Date = date,
            EndDate = date.AddHours(2),
            DurationMinutes = 120,
            Status = AppointmentStatus.Confirmed,
            DepositStatus = DepositStatus.Pending,
            DepositAmount = 50m,
        };
        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return appointment.Id;
    }

    private async Task SeedPayment(Guid appointmentId, PaymentStatus status)
    {
        _db.Payments.Add(new Payment
        {
            StudioId = _studioId,
            AppointmentId = appointmentId,
            ClientId = Guid.NewGuid(),
            Amount = 50m,
            Currency = "EUR",
            Status = status,
            Method = ClientPaymentMethod.Card,
            ProviderReferenceId = "pok_order_1",
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
