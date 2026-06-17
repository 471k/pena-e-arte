using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class RegisterStudioHandlerTests
{
    private readonly FakeDbContext _db   = FakeDbContext.Create();
    private readonly IJobScheduler _jobs = Substitute.For<IJobScheduler>();

    private RegisterStudioHandler CreateSut() =>
        new(_db, _jobs, Microsoft.Extensions.Logging.Abstractions.NullLogger<RegisterStudioHandler>.Instance);

    [Fact]
    public async Task Handle_NewSlug_ReturnsStudioResponse()
    {
        RegisterStudioRequest req = ValidRequest();

        StudioResponse result = await CreateSut().Handle(new RegisterStudioCommand(req), default);

        result.Name.Should().Be(req.Name);
        result.Slug.Should().Be(req.Slug);
        result.City.Should().Be(req.City);
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_NewSlug_PersistsStudioToDb()
    {
        RegisterStudioRequest req = ValidRequest();

        await CreateSut().Handle(new RegisterStudioCommand(req), default);

        _db.Studios.Should().ContainSingle(s => s.Slug == req.Slug);
    }

    [Fact]
    public async Task Handle_NewStudio_CreatesTrialingSubscription()
    {
        await CreateSut().Handle(new RegisterStudioCommand(ValidRequest()), default);

        Studio studio = _db.Studios.Single();
        _db.Subscriptions.Should().ContainSingle(sub =>
            sub.StudioId == studio.Id &&
            sub.Status   == SubscriptionStatus.Trialing);
    }

    [Fact]
    public async Task Handle_NewStudio_SetsTrialExpiresAt14DaysFromNow()
    {
        StudioResponse result = await CreateSut().Handle(new RegisterStudioCommand(ValidRequest()), default);

        result.TrialExpiresAt.Should().BeCloseTo(DateTime.UtcNow.AddDays(14), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_NewStudio_SetsGracePeriodEnd21DaysFromNow()
    {
        await CreateSut().Handle(new RegisterStudioCommand(ValidRequest()), default);

        Domain.Entities.Subscription sub = _db.Subscriptions.Single();
        sub.GracePeriodEnd.Should().BeCloseTo(DateTime.UtcNow.AddDays(21), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Handle_NewStudio_SchedulesAllThreeTrialJobs()
    {
        await CreateSut().Handle(new RegisterStudioCommand(ValidRequest()), default);

        _jobs.Received(1).ScheduleTrialExpiryWarning(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>());
        _jobs.Received(1).ScheduleTrialExpiry(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>());
        _jobs.Received(1).ScheduleGracePeriodEnd(Arg.Any<Guid>(), Arg.Any<DateTimeOffset>());
    }

    [Fact]
    public async Task Handle_SlugCollision_AppendsSuffixUntilUnique()
    {
        // Arrange
        _db.Studios.Add(new Studio { Name = "Existing",   Slug = "my-studio",   City = "Lisbon" });
        _db.Studios.Add(new Studio { Name = "Existing 2", Slug = "my-studio-2", City = "Lisbon" });
        await _db.SaveChangesAsync();

        // Act
        StudioResponse result = await CreateSut()
            .Handle(new RegisterStudioCommand(ValidRequest() with { Slug = "my-studio" }), default);

        // Assert
        result.Slug.Should().Be("my-studio-3");
        _db.Studios.Should().ContainSingle(s => s.Slug == "my-studio-3");
    }

    [Fact]
    public async Task Handle_NewStudio_IsActiveByDefault()
    {
        await CreateSut().Handle(new RegisterStudioCommand(ValidRequest()), default);

        _db.Studios.Single().IsActive.Should().BeTrue();
    }

    private static RegisterStudioRequest ValidRequest() =>
        new("Tinta & Alma", "tinta-alma", "Porto", 41.15, -8.61, "owner@tinta-alma.com");
}
