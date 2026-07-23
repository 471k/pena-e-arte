using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class StudioHandlerIntegrationTests(DatabaseFixture fixture)
{
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();

    // ── RegisterStudio ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RegisterStudio_NewSlug_PersistsStudioToDatabase()
    {
        string slug = UniqueSlug();
        StudioResponse result = await RunRegisterHandler(new("Tinta Viva", slug, "Lisboa", 38.7, -9.1, "owner@tintaviva.com", UniqueTestNipt()));

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        bool exists = await verify.Studios.AnyAsync(s => s.Id == result.Id);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterStudio_NewSlug_CreatesTrialingSubscription()
    {
        string slug = UniqueSlug();
        StudioResponse result = await RunRegisterHandler(new("Tinta Viva", slug, "Porto", 41.1, -8.6, "owner@tintaviva.com", UniqueTestNipt()));

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        Subscription? sub = await verify.Subscriptions.FirstOrDefaultAsync(s => s.StudioId == result.Id);

        sub.Should().NotBeNull();
        sub!.Status.Should().Be(SubscriptionStatus.Trialing);
    }

    [Fact]
    public async Task RegisterStudio_NewSlug_SetsCorrectTrialAndGraceDates()
    {
        string slug = UniqueSlug();
        StudioResponse result = await RunRegisterHandler(new("Test Studio", slug, "Braga", 41.5, -8.4, "owner@teststudio.com", UniqueTestNipt()));

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        Subscription? sub = await verify.Subscriptions.FirstOrDefaultAsync(s => s.StudioId == result.Id);

        sub!.TrialExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromSeconds(5));
        sub.GracePeriodEnd.Should().BeCloseTo(DateTime.UtcNow.AddDays(21), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task RegisterStudio_NewSlug_SchedulesAllThreeTrialJobs()
    {
        await RunRegisterHandler(new("Job Studio", UniqueSlug(), "Faro", 37.0, -7.9, "owner@jobstudio.com", UniqueTestNipt()));

        _jobs.Received(1).ScheduleTrialExpiryWarning(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>());
        _jobs.Received(1).ScheduleTrialExpiry(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>());
        _jobs.Received(1).ScheduleGracePeriodEnd(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task RegisterStudio_DuplicateSlug_AppendsSuffixUntilUnique()
    {
        string slug = UniqueSlug();
        await RunRegisterHandler(new("Studio One", slug, "Lisboa", 38.7, -9.1, "owner@one.com", UniqueTestNipt()));

        StudioResponse result = await RunRegisterHandler(new("Studio Two", slug, "Porto", 41.1, -8.6, "owner@two.com", UniqueTestNipt()));

        result.Slug.Should().Be($"{slug}-2");
    }

    [Fact]
    public async Task RegisterStudio_IsActiveByDefault()
    {
        string slug = UniqueSlug();
        StudioResponse result = await RunRegisterHandler(new("Active Studio", slug, "Setubal", 38.5, -8.9, "owner@activestudio.com", UniqueTestNipt()));

        await using AppDbContext verify = fixture.CreateDbContext(Guid.Empty);
        Studio? studio = await verify.Studios.FindAsync(result.Id);

        studio!.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterStudio_DuplicateNiptDifferentOwner_Returns409WithMessage()
    {
        string nipt = UniqueTestNipt();
        await RunRegisterHandler(new("First Studio", UniqueSlug(), "Lisboa", 38.7, -9.1, "owner-a@duplicatenipt.com", nipt));

        Func<Task> act = () => RunRegisterHandler(
            new("Second Studio", UniqueSlug(), "Porto", 41.1, -8.6, "owner-b@duplicatenipt.com", nipt));

        (await act.Should().ThrowAsync<DuplicateNiptException>())
            .WithMessage("*already registered under a different account*");
    }

    [Fact]
    public async Task RegisterStudio_DuplicateNiptSameOwnerEmail_SucceedsForMultiLocation()
    {
        string nipt        = UniqueTestNipt();
        string ownerEmail  = "owner@multilocation.com";
        await RunRegisterHandler(new("Main Location", UniqueSlug(), "Lisboa", 38.7, -9.1, ownerEmail, nipt));

        StudioResponse second = await RunRegisterHandler(
            new("Second Location", UniqueSlug(), "Porto", 41.1, -8.6, ownerEmail, nipt));

        second.Should().NotBeNull();
    }

    // ── GetStudioMap ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetStudioMap_ReturnsOnlyActiveStudios()
    {
        string activeSlug   = UniqueSlug();
        string inactiveSlug = UniqueSlug();

        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);
        seed.Studios.Add(new Studio { Name = "Active",   Slug = activeSlug,   City = "Lisbon", IsActive = true,  Latitude = 38.7, Longitude = -9.1 });
        seed.Studios.Add(new Studio { Name = "Inactive", Slug = inactiveSlug, City = "Porto",  IsActive = false, Latitude = 41.1, Longitude = -8.6 });
        await seed.SaveChangesAsync();

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetStudioMapHandler handler = new(db);
        List<StudioMapItemResponse> result = await handler.Handle(new GetStudioMapQuery(), default);

        result.Should().Contain(s => s.Slug == activeSlug);
        result.Should().NotContain(s => s.Slug == inactiveSlug);
    }

    [Fact]
    public async Task GetStudioMap_ReturnsCorrectCoordinates()
    {
        string slug = UniqueSlug();
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);
        seed.Studios.Add(new Studio
        {
            Name      = "Coord Studio",
            Slug      = slug,
            City      = "Lisboa",
            IsActive  = true,
            Latitude  = 38.716667,
            Longitude = -9.133333
        });
        await seed.SaveChangesAsync();

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        GetStudioMapHandler handler = new(db);
        List<StudioMapItemResponse> result = await handler.Handle(new GetStudioMapQuery(), default);

        StudioMapItemResponse? item = result.FirstOrDefault(s => s.Slug == slug);
        item.Should().NotBeNull();
        item!.Latitude.Should().BeApproximately(38.716667, 0.000001);
        item.Longitude.Should().BeApproximately(-9.133333, 0.000001);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────────

    private async Task<StudioResponse> RunRegisterHandler(RegisterStudioRequest req)
    {
        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        RegisterStudioHandler handler = new(db, _jobs, Microsoft.Extensions.Logging.Abstractions.NullLogger<RegisterStudioHandler>.Instance);
        return await handler.Handle(new RegisterStudioCommand(req), default);
    }

    private static string UniqueSlug() =>
        ("s-" + Guid.NewGuid().ToString("N")).Substring(0, 20);

    private static string UniqueTestNipt() => $"L{(uint)Guid.NewGuid().GetHashCode() % 100000000:D8}A";
}
