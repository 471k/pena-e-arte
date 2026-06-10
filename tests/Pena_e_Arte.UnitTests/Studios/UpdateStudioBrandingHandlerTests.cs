using FluentAssertions;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class UpdateStudioBrandingHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private UpdateStudioBrandingHandler CreateSut() => new(_db);

    private async Task<Studio> SeedStudioWithPlan(bool allowBrandingRemoval)
    {
        Plan plan = new()
        {
            Name                 = "Test Plan",
            AllowBrandingRemoval = allowBrandingRemoval,
        };
        _db.Plans.Add(plan);

        Studio studio = new()
        {
            Name                 = "Test Studio",
            Slug                 = "test-studio",
            City                 = "Lisboa",
            IsActive             = true,
            ShowPlatformBranding = true,
        };
        _db.Studios.Add(studio);

        Subscription subscription = new()
        {
            StudioId = studio.Id,
            PlanId   = plan.Id,
            Plan     = plan,
        };
        _db.Subscriptions.Add(subscription);
        studio.Subscription = subscription;

        await _db.SaveChangesAsync();
        return studio;
    }

    [Fact]
    public async Task Handle_ShowBranding_UpdatesFlag()
    {
        Studio studio = await SeedStudioWithPlan(allowBrandingRemoval: false);
        studio.ShowPlatformBranding = false;
        await _db.SaveChangesAsync();

        StudioResponse result = await CreateSut()
            .Handle(new UpdateStudioBrandingCommand(studio.Id, ShowPlatformBranding: true), default);

        result.ShowPlatformBranding.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_HideBranding_PlanAllows_UpdatesFlag()
    {
        Studio studio = await SeedStudioWithPlan(allowBrandingRemoval: true);

        StudioResponse result = await CreateSut()
            .Handle(new UpdateStudioBrandingCommand(studio.Id, ShowPlatformBranding: false), default);

        result.ShowPlatformBranding.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_HideBranding_PlanDisallows_ThrowsBusinessRuleViolationException()
    {
        Studio studio = await SeedStudioWithPlan(allowBrandingRemoval: false);

        Func<Task> act = () => CreateSut()
            .Handle(new UpdateStudioBrandingCommand(studio.Id, ShowPlatformBranding: false), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*plan*");
    }

    [Fact]
    public async Task Handle_StudioNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut()
            .Handle(new UpdateStudioBrandingCommand(Guid.NewGuid(), ShowPlatformBranding: true), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_HideBranding_PlanDisallows_DoesNotPersist()
    {
        Studio studio = await SeedStudioWithPlan(allowBrandingRemoval: false);

        try
        {
            await CreateSut()
                .Handle(new UpdateStudioBrandingCommand(studio.Id, ShowPlatformBranding: false), default);
        }
        catch { }

        Studio? persisted = await _db.Studios.FindAsync(studio.Id);
        persisted!.ShowPlatformBranding.Should().BeTrue();
    }
}
