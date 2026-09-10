using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.GiftCards.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.GiftCards;

public class RedeemGiftCardHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _clientId = Guid.NewGuid();

    public RedeemGiftCardHandlerTests()
    {
        _tenant.StudioId.Returns(_studioId);
        _currentUser.Role.Returns("owner");
    }

    private RedeemGiftCardHandler CreateSut() => new(_db, _tenant, _currentUser);

    [Fact]
    public async Task Handle_PartialRedemption_LeavesCorrectRemainingBalance()
    {
        GiftCard card = await SeedGiftCard(100m, GiftCardStatus.Active);
        Guid apptId = await SeedAppointment(30m);

        await CreateSut().Handle(new RedeemGiftCardCommand(new RedeemGiftCardRequest(card.Code, apptId, 30m)), default);

        _db.GiftCards.Single(g => g.Id == card.Id).RemainingBalance.Should().Be(70m);
    }

    [Fact]
    public async Task Handle_PartialRedemption_StaysActive()
    {
        GiftCard card = await SeedGiftCard(100m, GiftCardStatus.Active);
        Guid apptId = await SeedAppointment(30m);

        await CreateSut().Handle(new RedeemGiftCardCommand(new RedeemGiftCardRequest(card.Code, apptId, 30m)), default);

        _db.GiftCards.Single(g => g.Id == card.Id).Status.Should().Be(GiftCardStatus.Active);
    }

    [Fact]
    public async Task Handle_FullRedemption_MarksRedeemed()
    {
        GiftCard card = await SeedGiftCard(50m, GiftCardStatus.Active);
        Guid apptId = await SeedAppointment(50m);

        await CreateSut().Handle(new RedeemGiftCardCommand(new RedeemGiftCardRequest(card.Code, apptId, 50m)), default);

        _db.GiftCards.Single(g => g.Id == card.Id).Status.Should().Be(GiftCardStatus.Redeemed);
    }

    [Fact]
    public async Task Handle_RedemptionCoversFullDeposit_MarksAppointmentDepositPaid()
    {
        GiftCard card = await SeedGiftCard(50m, GiftCardStatus.Active);
        Guid apptId = await SeedAppointment(50m);

        await CreateSut().Handle(new RedeemGiftCardCommand(new RedeemGiftCardRequest(card.Code, apptId, 50m)), default);

        Appointment appt = _db.Appointments.Single(a => a.Id == apptId);
        appt.DepositAmount.Should().Be(0m);
        appt.DepositStatus.Should().Be(DepositStatus.Paid);
    }

    [Fact]
    public async Task Handle_AmountExceedsRemainingBalance_Throws()
    {
        GiftCard card = await SeedGiftCard(20m, GiftCardStatus.Active);
        Guid apptId = await SeedAppointment(50m);

        Func<Task> act = () => CreateSut().Handle(
            new RedeemGiftCardCommand(new RedeemGiftCardRequest(card.Code, apptId, 30m)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_PendingCard_Throws()
    {
        GiftCard card = await SeedGiftCard(50m, GiftCardStatus.Pending);
        Guid apptId = await SeedAppointment(50m);

        Func<Task> act = () => CreateSut().Handle(
            new RedeemGiftCardCommand(new RedeemGiftCardRequest(card.Code, apptId, 10m)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_VoidedCard_Throws()
    {
        GiftCard card = await SeedGiftCard(50m, GiftCardStatus.Voided);
        Guid apptId = await SeedAppointment(50m);

        Func<Task> act = () => CreateSut().Handle(
            new RedeemGiftCardCommand(new RedeemGiftCardRequest(card.Code, apptId, 10m)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    private async Task<GiftCard> SeedGiftCard(decimal balance, GiftCardStatus status)
    {
        GiftCard card = new()
        {
            StudioId = _studioId,
            Code = "TESTCODE1234",
            InitialBalance = balance,
            RemainingBalance = balance,
            PurchaserEmail = "buyer@example.com",
            Status = status,
            Provider = "pok",
        };
        _db.GiftCards.Add(card);
        await _db.SaveChangesAsync(default);
        return card;
    }

    private async Task<Guid> SeedAppointment(decimal depositAmount)
    {
        _db.Clients.Add(new Client { Id = _clientId, StudioId = _studioId, FirstName = "C", LastName = "L", Email = "c@l.com" });
        Appointment appt = new()
        {
            StudioId = _studioId,
            ClientId = _clientId,
            Date = DateTime.UtcNow.AddDays(3),
            EndDate = DateTime.UtcNow.AddDays(3).AddMinutes(60),
            DurationMinutes = 60,
            Status = AppointmentStatus.Pending,
            DepositStatus = DepositStatus.Pending,
            DepositAmount = depositAmount,
        };
        _db.Appointments.Add(appt);
        await _db.SaveChangesAsync(default);
        return appt.Id;
    }
}
