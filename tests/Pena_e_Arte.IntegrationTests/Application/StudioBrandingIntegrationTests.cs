using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class StudioBrandingIntegrationTests(DatabaseFixture fixture)
{
    // ── UpdateStudioBranding ─────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateBranding_ShowTrue_PersistsToDatabase()
    {
        (Studio studio, _) = await SeedStudioWithPlan(allowBrandingRemoval: true);
        studio.UpdateBranding(false);
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);
        seed.Studios.Update(studio);
        await seed.SaveChangesAsync();

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        UpdateStudioBrandingHandler handler = new(db, MakeTenant(studio.Id));
        await handler.Handle(new UpdateStudioBrandingCommand(studio.Id, ShowPlatformBranding: true), default);

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        Studio? persisted = await verify.Studios.FindAsync(studio.Id);
        persisted!.ShowPlatformBranding.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateBranding_HideBranding_PlanAllows_PersistsToDatabase()
    {
        (Studio studio, _) = await SeedStudioWithPlan(allowBrandingRemoval: true);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        UpdateStudioBrandingHandler handler = new(db, MakeTenant(studio.Id));
        StudioResponse result = await handler.Handle(
            new UpdateStudioBrandingCommand(studio.Id, ShowPlatformBranding: false), default);

        result.ShowPlatformBranding.Should().BeFalse();

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        Studio? persisted = await verify.Studios.FindAsync(studio.Id);
        persisted!.ShowPlatformBranding.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateBranding_HideBranding_PlanDisallows_ThrowsAndDoesNotPersist()
    {
        (Studio studio, _) = await SeedStudioWithPlan(allowBrandingRemoval: false);

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        UpdateStudioBrandingHandler handler = new(db, MakeTenant(studio.Id));

        Func<Task> act = () => handler.Handle(
            new UpdateStudioBrandingCommand(studio.Id, ShowPlatformBranding: false), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        Studio? persisted = await verify.Studios.FindAsync(studio.Id);
        persisted!.ShowPlatformBranding.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateBranding_NoSubscription_HideBranding_Throws()
    {
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);
        Studio studio = new()
        {
            Name     = "No Sub Studio",
            Slug     = UniqueSlug(),
            City     = "Porto",
            IsActive = true,
        };
        seed.Studios.Add(studio);
        await seed.SaveChangesAsync();

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        UpdateStudioBrandingHandler handler = new(db, MakeTenant(studio.Id));

        Func<Task> act = () => handler.Handle(
            new UpdateStudioBrandingCommand(studio.Id, ShowPlatformBranding: false), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<(Studio studio, Plan plan)> SeedStudioWithPlan(bool allowBrandingRemoval)
    {
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);

        Plan plan = new()
        {
            Name                 = "Test Plan",
            AllowBrandingRemoval = allowBrandingRemoval,
        };
        seed.Plans.Add(plan);

        Studio studio = new()
        {
            Name     = "Branding Test Studio",
            Slug     = UniqueSlug(),
            City     = "Lisboa",
            IsActive = true,
        };
        seed.Studios.Add(studio);

        Subscription subscription = new()
        {
            StudioId = studio.Id,
            PlanId   = plan.Id,
        };
        seed.Subscriptions.Add(subscription);

        await seed.SaveChangesAsync();
        return (studio, plan);
    }

    private static string UniqueSlug() =>
        ("b-" + Guid.NewGuid().ToString("N")).Substring(0, 20);

    private static ICurrentTenant MakeTenant(Guid studioId)
    {
        ICurrentTenant tenant = Substitute.For<ICurrentTenant>();
        tenant.StudioId.Returns(studioId);
        return tenant;
    }
}
