using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Public;

public class GetPublicDepositRuleHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetPublicDepositRuleHandler CreateSut() => new(_db);

    private static Studio MakeStudio(string slug = "guest-studio") => new()
    {
        Name = "Guest Studio", Slug = slug, City = "Porto", IsActive = true, IsPublished = true,
    };

    [Fact]
    public async Task Handle_OneActiveRule_ReturnsIt()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        _db.DepositRules.Add(new DepositRule
        {
            StudioId = studio.Id, Name = "Standard", AmountFixed = 50, IsActive = true,
        });
        await _db.SaveChangesAsync();

        PublicDepositRuleResponse? result = await CreateSut().Handle(new GetPublicDepositRuleQuery(studio.Slug), default);

        result.Should().NotBeNull();
        result!.Name.Should().Be("Standard");
        result.AmountFixed.Should().Be(50);
    }

    [Fact]
    public async Task Handle_MultipleActiveRules_ReturnsMostRecentlyUpdated()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        _db.DepositRules.Add(new DepositRule
        {
            StudioId = studio.Id, Name = "Old", AmountFixed = 20, IsActive = true,
            UpdatedAt = DateTime.UtcNow.AddDays(-2),
        });
        _db.DepositRules.Add(new DepositRule
        {
            StudioId = studio.Id, Name = "Newest", AmountFixed = 40, IsActive = true,
            UpdatedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        PublicDepositRuleResponse? result = await CreateSut().Handle(new GetPublicDepositRuleQuery(studio.Slug), default);

        result!.Name.Should().Be("Newest");
    }

    [Fact]
    public async Task Handle_NoActiveRule_ReturnsNull()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        _db.DepositRules.Add(new DepositRule
        {
            StudioId = studio.Id, Name = "Retired", AmountFixed = 20, IsActive = false,
        });
        await _db.SaveChangesAsync();

        PublicDepositRuleResponse? result = await CreateSut().Handle(new GetPublicDepositRuleQuery(studio.Slug), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Handle_UnknownSlug_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetPublicDepositRuleQuery("no-such-slug"), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
