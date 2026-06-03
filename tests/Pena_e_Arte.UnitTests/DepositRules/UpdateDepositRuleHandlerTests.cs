using FluentAssertions;
using Pena_e_Arte.Application.DepositRules.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.DepositRules;

public class UpdateDepositRuleHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();

    private UpdateDepositRuleHandler CreateSut() => new(_db);

    private async Task<DepositRule> SeedRule(string name, decimal? fixed_, decimal? percent, bool isActive)
    {
        DepositRule rule = new()
        {
            StudioId      = _studioId,
            Name          = name,
            AmountFixed   = fixed_,
            AmountPercent = percent,
            IsActive      = isActive
        };
        _db.DepositRules.Add(rule);
        await _db.SaveChangesAsync();
        return rule;
    }

    [Fact]
    public async Task Handle_ExistingRule_ReturnsUpdatedResponse()
    {
        DepositRule rule = await SeedRule("Old Name", 30m, null, false);
        UpdateDepositRuleRequest req = new("New Name", null, 25m, false);

        DepositRuleResponse result = await CreateSut().Handle(new UpdateDepositRuleCommand(rule.Id, req), default);

        result.Name.Should().Be("New Name");
        result.AmountFixed.Should().BeNull();
        result.AmountPercent.Should().Be(25m);
    }

    [Fact]
    public async Task Handle_ExistingRule_PersistsChanges()
    {
        DepositRule rule = await SeedRule("Old Name", 30m, null, false);
        UpdateDepositRuleRequest req = new("Updated Name", 75m, null, true);

        await CreateSut().Handle(new UpdateDepositRuleCommand(rule.Id, req), default);

        DepositRule updated = _db.DepositRules.Single(r => r.Id == rule.Id);
        updated.Name.Should().Be("Updated Name");
        updated.AmountFixed.Should().Be(75m);
        updated.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NonExistentId_ThrowsNotFoundException()
    {
        UpdateDepositRuleRequest req = new("X", 10m, null, false);

        Func<Task> act = () => CreateSut().Handle(new UpdateDepositRuleCommand(Guid.NewGuid(), req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_SetActive_DeactivatesOtherActiveRules()
    {
        DepositRule active = await SeedRule("Active Rule", 30m, null, true);
        DepositRule target = await SeedRule("Target Rule", 50m, null, false);

        UpdateDepositRuleRequest req = new("Target Rule", 50m, null, true);
        await CreateSut().Handle(new UpdateDepositRuleCommand(target.Id, req), default);

        _db.DepositRules.Single(r => r.Id == active.Id).IsActive.Should().BeFalse();
        _db.DepositRules.Single(r => r.Id == target.Id).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_AlreadyActive_DoesNotDeactivateSelf()
    {
        DepositRule rule = await SeedRule("Active Rule", 30m, null, true);
        UpdateDepositRuleRequest req = new("Active Rule", 30m, null, true);

        Func<Task> act = () => CreateSut().Handle(new UpdateDepositRuleCommand(rule.Id, req), default);

        await act.Should().NotThrowAsync();
        _db.DepositRules.Single(r => r.Id == rule.Id).IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_Update_SetsUpdatedAt()
    {
        DepositRule rule = await SeedRule("Rule", 40m, null, false);
        DateTime before = rule.UpdatedAt;

        await Task.Delay(10);
        UpdateDepositRuleRequest req = new("Rule", 40m, null, false);
        await CreateSut().Handle(new UpdateDepositRuleCommand(rule.Id, req), default);

        _db.DepositRules.Single(r => r.Id == rule.Id).UpdatedAt.Should().BeAfter(before);
    }
}
