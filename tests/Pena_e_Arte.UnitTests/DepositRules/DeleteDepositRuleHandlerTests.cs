using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.DepositRules.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.DepositRules;

public class DeleteDepositRuleHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();

    private DeleteDepositRuleHandler CreateSut() => new(_db);

    private async Task<DepositRule> SeedRule()
    {
        DepositRule rule = new() { StudioId = _studioId, Name = "Test Rule", AmountFixed = 50m };
        _db.DepositRules.Add(rule);
        await _db.SaveChangesAsync();
        return rule;
    }

    [Fact]
    public async Task Handle_ExistingRule_SetsDeletedAt()
    {
        DepositRule rule = await SeedRule();

        await CreateSut().Handle(new DeleteDepositRuleCommand(rule.Id), default);

        _db.DepositRules.IgnoreQueryFilters()
            .Single(r => r.Id == rule.Id)
            .DeletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ExistingRule_DoesNotHardDelete()
    {
        DepositRule rule = await SeedRule();

        await CreateSut().Handle(new DeleteDepositRuleCommand(rule.Id), default);

        _db.DepositRules.IgnoreQueryFilters().Should().ContainSingle(r => r.Id == rule.Id);
    }

    [Fact]
    public async Task Handle_NonExistentId_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new DeleteDepositRuleCommand(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
