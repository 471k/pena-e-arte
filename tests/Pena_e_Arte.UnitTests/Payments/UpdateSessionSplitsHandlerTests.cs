using FluentAssertions;
using Pena_e_Arte.Application.Payments.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Payments;

public class UpdateSessionSplitsHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();

    private UpdateSessionSplitsHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ValidSplits_ReturnsSplitsInResponse()
    {
        Guid paymentId = await SeedPayment(300m);

        PaymentResponse result = await CreateSut().Handle(
            new UpdateSessionSplitsCommand(paymentId, new UpdateSessionSplitsRequest(
            [
                new SessionSplitItem("Deposit",  100m),
                new SessionSplitItem("Session 1", 100m),
                new SessionSplitItem("Final",    100m)
            ])), default);

        result.SessionSplits.Should().HaveCount(3);
        result.SessionSplits.Should().Contain(s => s.Label == "Deposit"   && s.Amount == 100m);
        result.SessionSplits.Should().Contain(s => s.Label == "Session 1" && s.Amount == 100m);
        result.SessionSplits.Should().Contain(s => s.Label == "Final"     && s.Amount == 100m);
    }

    [Fact]
    public async Task Handle_ValidSplits_PersistsSplitsToDb()
    {
        Guid paymentId = await SeedPayment(200m);

        await CreateSut().Handle(
            new UpdateSessionSplitsCommand(paymentId, new UpdateSessionSplitsRequest(
            [
                new SessionSplitItem("Deposit", 100m),
                new SessionSplitItem("Final",   100m)
            ])), default);

        _db.SessionSplits.Count(ss => ss.PaymentId == paymentId && ss.DeletedAt == null)
            .Should().Be(2);
    }

    [Fact]
    public async Task Handle_ExistingSplits_SoftDeletesOldOnesAndAddsNew()
    {
        Guid paymentId = await SeedPayment(100m);
        _db.SessionSplits.Add(new SessionSplit
        {
            StudioId  = _studioId,
            PaymentId = paymentId,
            Label     = "OldSplit",
            Amount    = 100m
        });
        await _db.SaveChangesAsync();

        await CreateSut().Handle(
            new UpdateSessionSplitsCommand(paymentId, new UpdateSessionSplitsRequest(
            [
                new SessionSplitItem("NewSplit", 100m)
            ])), default);

        _db.SessionSplits.Count(ss => ss.PaymentId == paymentId && ss.DeletedAt == null).Should().Be(1);
        _db.SessionSplits.Single(ss => ss.DeletedAt == null).Label.Should().Be("NewSplit");
        _db.SessionSplits.Single(ss => ss.Label == "OldSplit").DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_PaymentNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new UpdateSessionSplitsCommand(Guid.NewGuid(), new UpdateSessionSplitsRequest(
            [
                new SessionSplitItem("Split", 100m)
            ])), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_SplitsTotalDoesNotMatchPaymentAmount_ThrowsBusinessRuleViolationException()
    {
        Guid paymentId = await SeedPayment(300m);

        Func<Task> act = () => CreateSut().Handle(
            new UpdateSessionSplitsCommand(paymentId, new UpdateSessionSplitsRequest(
            [
                new SessionSplitItem("Split A", 100m),
                new SessionSplitItem("Split B", 150m)
            ])), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*250.00*300.00*");
    }

    private async Task<Guid> SeedPayment(decimal amount)
    {
        Payment payment = new()
        {
            StudioId      = _studioId,
            AppointmentId = Guid.NewGuid(),
            ClientId      = Guid.NewGuid(),
            Amount        = amount,
            Status        = PaymentStatus.Pending
        };
        _db.Payments.Add(payment);
        await _db.SaveChangesAsync();
        return payment.Id;
    }
}
