using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.DepositRules.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.DepositRules;

public class CreateDepositRuleHandlerTests
{
    private readonly FakeDbContext  _db       = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant   = Substitute.For<ICurrentTenant>();
    private readonly Guid           _studioId = Guid.NewGuid();

    public CreateDepositRuleHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private CreateDepositRuleHandler CreateSut() => new(_db, _tenant);

    [Fact]
    public async Task Handle_FixedAmountRule_ReturnsDepositRuleResponse()
    {
        CreateDepositRuleRequest req = new("Standard Deposit", 50m, null, false);

        DepositRuleResponse result = await CreateSut().Handle(new CreateDepositRuleCommand(req), default);

        result.Name.Should().Be("Standard Deposit");
        result.AmountFixed.Should().Be(50m);
        result.AmountPercent.Should().BeNull();
        result.IsActive.Should().BeFalse();
        result.StudioId.Should().Be(_studioId);
    }

    [Fact]
    public async Task Handle_PercentAmountRule_ReturnsDepositRuleResponse()
    {
        CreateDepositRuleRequest req = new("Percentage Deposit", null, 20m, false);

        DepositRuleResponse result = await CreateSut().Handle(new CreateDepositRuleCommand(req), default);

        result.AmountFixed.Should().BeNull();
        result.AmountPercent.Should().Be(20m);
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsRuleToDb()
    {
        CreateDepositRuleRequest req = new("Standard Deposit", 50m, null, false);

        await CreateSut().Handle(new CreateDepositRuleCommand(req), default);

        _db.DepositRules.Should().ContainSingle(r => r.Name == "Standard Deposit" && r.StudioId == _studioId);
    }

    [Fact]
    public async Task Handle_IsActiveTrue_DeactivatesOtherRules()
    {
        DepositRule existing = new() { StudioId = _studioId, Name = "Old Rule", AmountFixed = 30m, IsActive = true };
        _db.DepositRules.Add(existing);
        await _db.SaveChangesAsync();

        CreateDepositRuleRequest req = new("New Active Rule", 50m, null, true);
        await CreateSut().Handle(new CreateDepositRuleCommand(req), default);

        _db.DepositRules.Single(r => r.Id == existing.Id).IsActive.Should().BeFalse();
        _db.DepositRules.Single(r => r.Name == "New Active Rule").IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_IsActiveFalse_DoesNotDeactivateOtherRules()
    {
        DepositRule existing = new() { StudioId = _studioId, Name = "Old Rule", AmountFixed = 30m, IsActive = true };
        _db.DepositRules.Add(existing);
        await _db.SaveChangesAsync();

        CreateDepositRuleRequest req = new("Inactive Rule", 50m, null, false);
        await CreateSut().Handle(new CreateDepositRuleCommand(req), default);

        _db.DepositRules.Single(r => r.Id == existing.Id).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ValidRequest_SetsCorrectStudioId()
    {
        CreateDepositRuleRequest req = new("Studio Rule", null, 15m, false);

        DepositRuleResponse result = await CreateSut().Handle(new CreateDepositRuleCommand(req), default);

        result.StudioId.Should().Be(_studioId);
    }

    [Fact]
    public async Task Handle_OmittingCancellationFields_DefaultsToNullAndZero()
    {
        CreateDepositRuleRequest req = new("Standard Deposit", 50m, null, false);

        DepositRuleResponse result = await CreateSut().Handle(new CreateDepositRuleCommand(req), default);

        result.CancellationWindowHours.Should().BeNull();
        result.RefundPercentOnLateCancel.Should().Be(0);
    }

    [Fact]
    public async Task Handle_WithCancellationFields_PersistsThem()
    {
        CreateDepositRuleRequest req = new("Standard Deposit", 50m, null, false, 48, 50);

        DepositRuleResponse result = await CreateSut().Handle(new CreateDepositRuleCommand(req), default);

        result.CancellationWindowHours.Should().Be(48);
        result.RefundPercentOnLateCancel.Should().Be(50);
        _db.DepositRules.Single(r => r.Name == "Standard Deposit").CancellationWindowHours.Should().Be(48);
    }
}
