using FluentAssertions;
using MediatR;
using NSubstitute;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class ConfirmCashDepositHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly Guid _studioId = Guid.NewGuid();

    public ConfirmCashDepositHandlerTests()
    {
        _currentUser.Role.Returns("owner");
        _currentUser.UserId.Returns(Guid.NewGuid());
    }

    private ConfirmCashDepositHandler CreateSut() => new(_db, _currentUser, _sender);

    [Fact]
    public async Task Handle_CashPendingPayment_ReturnsPaidStatus()
    {
        await SeedStudio();
        Guid paymentId = await SeedCashPayment();

        PaymentResponse result = await CreateSut().Handle(new ConfirmCashDepositCommand(paymentId), default);

        result.Status.Should().Be(PaymentStatus.Paid.ToString());
    }

    [Fact]
    public async Task Handle_CashPendingPayment_PersistsPaidStatus()
    {
        await SeedStudio();
        Guid paymentId = await SeedCashPayment();

        await CreateSut().Handle(new ConfirmCashDepositCommand(paymentId), default);

        _db.Payments.Single(p => p.Id == paymentId).Status.Should().Be(PaymentStatus.Paid);
    }

    [Fact]
    public async Task Handle_NotCashMethod_ThrowsBusinessRuleViolationException()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(ClientPaymentMethod.Card, PaymentStatus.Pending);

        Func<Task> act = () => CreateSut().Handle(new ConfirmCashDepositCommand(paymentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_AlreadyConfirmed_ThrowsBusinessRuleViolationException()
    {
        await SeedStudio();
        Guid paymentId = await SeedPayment(ClientPaymentMethod.Cash, PaymentStatus.Paid);

        Func<Task> act = () => CreateSut().Handle(new ConfirmCashDepositCommand(paymentId), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_PaymentNotFound_ThrowsNotFoundException()
    {
        await SeedStudio();

        Func<Task> act = () => CreateSut().Handle(new ConfirmCashDepositCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void ConfirmCashDepositCommand_IsAuditableForCashDepositConfirmed()
    {
        Guid paymentId = Guid.NewGuid();
        IAuditableCommand command = new ConfirmCashDepositCommand(paymentId);

        command.AuditAction.Should().Be("Payment.CashDepositConfirmed");
        command.AuditTargetType.Should().Be("Payment");
        command.AuditTargetId.Should().Be(paymentId);
    }

    private async Task SeedStudio()
    {
        _db.Studios.Add(new Studio
        {
            Id = _studioId,
            Name = "Test",
            Slug = "test"
        });
        await _db.SaveChangesAsync();
    }

    private async Task<Guid> SeedCashPayment() => await SeedPayment(ClientPaymentMethod.Cash, PaymentStatus.CashPending);

    private async Task<Guid> SeedPayment(ClientPaymentMethod method, PaymentStatus status)
    {
        Client client = new()
        {
            StudioId = _studioId,
            FirstName = "Test",
            LastName = "Client",
            Email = $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        Payment payment = new()
        {
            StudioId = _studioId,
            AppointmentId = Guid.NewGuid(),
            ClientId = client.Id,
            Amount = 100m,
            Status = status,
            Method = method
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }
}
