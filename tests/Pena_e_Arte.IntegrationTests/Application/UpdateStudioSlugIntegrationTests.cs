using FluentAssertions;
using MediatR;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class UpdateStudioSlugIntegrationTests(DatabaseFixture fixture)
{
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();

    // ── UpdateStudioSlug ─────────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateStudioSlug_ValidSlug_UpdatesSlugInDatabase()
    {
        // Arrange
        string originalSlug = UniqueSlug();
        string newSlug      = UniqueSlug();

        StudioResponse registered = await RunRegisterHandler(new("Slug Studio", originalSlug, "Lisboa", 38.7, -9.1, "owner@slugstudio.com", UniqueTestNipt()));

        // Act
        await RunUpdateSlugHandler(registered.Id, newSlug);

        // Assert
        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        Studio? studio = await verify.Studios.FindAsync(registered.Id);

        studio!.Slug.Should().Be(newSlug);
        studio.SlugLockedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateStudioSlug_SlugLockedAfterFirstChange_PreventsSecondChange()
    {
        // Arrange — register and change slug once
        string originalSlug = UniqueSlug();
        string secondSlug   = UniqueSlug();
        string thirdSlug    = UniqueSlug();

        StudioResponse registered = await RunRegisterHandler(new("Lock Studio", originalSlug, "Porto", 41.1, -8.6, "owner@lockstudio.com", UniqueTestNipt()));
        await RunUpdateSlugHandler(registered.Id, secondSlug);

        // Act — second change should be rejected
        Func<Task> act = () => RunUpdateSlugHandler(registered.Id, thirdSlug);

        // Assert
        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*already been changed*");
    }

    [Fact]
    public async Task UpdateStudioSlug_DuplicateSlug_ThrowsBusinessRuleViolationException()
    {
        // Arrange — seed a second studio that already owns the target slug
        string takenSlug = UniqueSlug();
        await RunRegisterHandler(new("Taken Studio", takenSlug, "Braga", 41.5, -8.4, "owner@taken.com", UniqueTestNipt()));

        string ownSlug  = UniqueSlug();
        StudioResponse mine = await RunRegisterHandler(new("My Studio", ownSlug, "Faro", 37.0, -7.9, "owner@mine.com", UniqueTestNipt()));

        // Act
        Func<Task> act = () => RunUpdateSlugHandler(mine.Id, takenSlug);

        // Assert
        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*already taken*");
    }

    [Fact]
    public async Task UpdateStudioSlug_SecondChange_ThrowsBusinessRuleViolationException()
    {
        // Arrange
        string original = UniqueSlug();
        string second   = UniqueSlug();
        string third    = UniqueSlug();

        StudioResponse studio = await RunRegisterHandler(new("Two-Change", original, "Evora", 38.5, -7.9, "owner@twochange.com", UniqueTestNipt()));
        await RunUpdateSlugHandler(studio.Id, second);

        // Act
        Func<Task> act = () => RunUpdateSlugHandler(studio.Id, third);

        // Assert
        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<StudioResponse> RunRegisterHandler(RegisterStudioRequest req)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        RegisterStudioHandler handler = new(db, _jobs, Microsoft.Extensions.Logging.Abstractions.NullLogger<RegisterStudioHandler>.Instance);
        return await handler.Handle(new RegisterStudioCommand(req), default);
    }

    private async Task<Unit> RunUpdateSlugHandler(Guid studioId, string newSlug)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        UpdateStudioSlugHandler handler = new(db);
        return await handler.Handle(new UpdateStudioSlugCommand(studioId, newSlug), default);
    }

    private static string UniqueSlug() =>
        ("slug-" + Guid.NewGuid().ToString("N")).Substring(0, 20);

    private static string UniqueTestNipt() => $"L{(uint)Guid.NewGuid().GetHashCode() % 100000000:D8}A";
}
