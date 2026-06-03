using FluentAssertions;
using Pena_e_Arte.Application.DepositRules.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.DepositRules;

public class GetDepositRulesHandlerTests
{
    private readonly FakeDbContext _db       = FakeDbContext.Create();
    private readonly Guid          _studioId = Guid.NewGuid();

    private GetDepositRulesHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_NoRules_ReturnsEmptyList()
    {
        List<DepositRuleResponse> result = await CreateSut().Handle(new GetDepositRulesQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MultipleRules_ReturnsAllRules()
    {
        await SeedRules(
            ("Rule A", 30m, null, false),
            ("Rule B", null, 20m, false));

        List<DepositRuleResponse> result = await CreateSut().Handle(new GetDepositRulesQuery(), default);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_ActiveRuleFirst_OrdersCorrectly()
    {
        await SeedRules(
            ("Inactive", 30m, null, false),
            ("Active",   50m, null, true));

        List<DepositRuleResponse> result = await CreateSut().Handle(new GetDepositRulesQuery(), default);

        result[0].Name.Should().Be("Active");
        result[1].Name.Should().Be("Inactive");
    }

    // GetDepositRule (single) ────────────────────────────────────────────────────

    [Fact]
    public async Task GetSingle_ExistingRule_ReturnsRule()
    {
        await SeedRules(("My Rule", 60m, null, true));
        Guid id = _db.DepositRules.Single(r => r.Name == "My Rule").Id;

        GetDepositRuleHandler handler = new(_db);
        DepositRuleResponse result = await handler.Handle(new GetDepositRuleQuery(id), default);

        result.Name.Should().Be("My Rule");
        result.AmountFixed.Should().Be(60m);
        result.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetSingle_NonExistentId_ThrowsNotFoundException()
    {
        GetDepositRuleHandler handler = new(_db);

        Func<Task> act = () => handler.Handle(new GetDepositRuleQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task SeedRules(params (string Name, decimal? Fixed, decimal? Percent, bool Active)[] rules)
    {
        foreach ((string name, decimal? fixed_, decimal? percent, bool active) in rules)
            _db.DepositRules.Add(new DepositRule
            {
                StudioId      = _studioId,
                Name          = name,
                AmountFixed   = fixed_,
                AmountPercent = percent,
                IsActive      = active
            });

        await _db.SaveChangesAsync();
    }
}
