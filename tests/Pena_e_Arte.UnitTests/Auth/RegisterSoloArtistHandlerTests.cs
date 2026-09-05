using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Auth;

public class RegisterSoloArtistHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IEmailRenderer _emailRenderer = Substitute.For<IEmailRenderer>();
    private readonly INotificationService _notifications = Substitute.For<INotificationService>();
    private readonly IAppSettings _appSettings = Substitute.For<IAppSettings>();
    private readonly Guid _userId = Guid.NewGuid();

    public RegisterSoloArtistHandlerTests()
    {
        _appSettings.BaseUrl.Returns(string.Empty);
        _identity.GenerateEmailConfirmationTokenAsync(Arg.Any<Guid>()).Returns("token");
        _db.Plans.Add(new Plan { Id = Guid.NewGuid(), Name = "Free", MaxArtists = 1 });
        _db.SaveChangesAsync().GetAwaiter().GetResult();
    }

    private RegisterSoloArtistHandler CreateSut() => new(
        _db, _identity, _emailRenderer, _notifications, _appSettings,
        NullLogger<RegisterSoloArtistHandler>.Instance);

    private void IdentitySucceeds() =>
        _identity.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string?>())
                 .Returns((true, _userId, Array.Empty<string>()));

    private static RegisterSoloArtistRequest ValidRequest() =>
        new("solo@example.com", "Password1!", "Jane", "Doe");

    [Fact]
    public async Task Handle_ValidRequest_CreatesSoloUnpublishedStudioOnFreePlan()
    {
        IdentitySucceeds();

        await CreateSut().Handle(new RegisterSoloArtistCommand(ValidRequest()), default);

        Studio studio = _db.Studios.Single();
        studio.IsSolo.Should().BeTrue();
        studio.IsPublished.Should().BeFalse();
        studio.Nipt.Should().BeNull();
        studio.OwnerEmail.Should().Be("solo@example.com");
        studio.IsActive.Should().BeTrue();

        Subscription subscription = _db.Subscriptions.Single();
        subscription.StudioId.Should().Be(studio.Id);
        subscription.Status.Should().Be(SubscriptionStatus.Active);
        subscription.TrialExpiresAt.Should().BeNull();
        subscription.CurrentPeriodEnd.Should().BeCloseTo(DateTime.UtcNow.AddYears(50), TimeSpan.FromMinutes(5));
        subscription.PlanId.Should().Be(_db.Plans.Single().Id);
    }

    [Fact]
    public async Task Handle_ValidRequest_CreatesOwnerRoleIdentityUser()
    {
        IdentitySucceeds();

        await CreateSut().Handle(new RegisterSoloArtistCommand(ValidRequest()), default);

        Studio studio = _db.Studios.Single();
        await _identity.Received(1).CreateUserAsync("solo@example.com", "Password1!", "owner", studio.Id, "Jane");
    }

    [Fact]
    public async Task Handle_IdentityFailure_ThrowsBusinessRuleViolationException()
    {
        _identity.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string?>())
                 .Returns((false, Guid.Empty, new[] { "Email already taken." }));

        Func<Task> act = () => CreateSut().Handle(new RegisterSoloArtistCommand(ValidRequest()), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>()
            .WithMessage("*Email already taken*");
    }

    [Fact]
    public async Task Handle_IdentityFailure_DoesNotPersistOrphanedStudioOrSubscription()
    {
        // Identity user creation must happen before the Studio/Subscription are saved — otherwise
        // a failure here would leave a Studio+Subscription committed with no owning user.
        _identity.CreateUserAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Guid?>(), Arg.Any<string?>())
                 .Returns((false, Guid.Empty, new[] { "Email already taken." }));

        Func<Task> act = () => CreateSut().Handle(new RegisterSoloArtistCommand(ValidRequest()), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
        _db.Studios.Should().BeEmpty();
        _db.Subscriptions.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DuplicateStudioName_SuffixesSlug()
    {
        IdentitySucceeds();

        await CreateSut().Handle(new RegisterSoloArtistCommand(ValidRequest()), default);
        await CreateSut().Handle(
            new RegisterSoloArtistCommand(new RegisterSoloArtistRequest("solo2@example.com", "Password1!", "Jane", "Doe")),
            default);

        _db.Studios.Select(s => s.Slug).Distinct().Count().Should().Be(2);
    }

    [Fact]
    public async Task Handle_NoFreePlanSeeded_ThrowsInvalidOperationException()
    {
        FakeDbContext dbWithoutPlan = FakeDbContext.Create();
        RegisterSoloArtistHandler sut = new(
            dbWithoutPlan, _identity, _emailRenderer, _notifications, _appSettings,
            NullLogger<RegisterSoloArtistHandler>.Instance);
        IdentitySucceeds();

        Func<Task> act = () => sut.Handle(new RegisterSoloArtistCommand(ValidRequest()), default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
