using FluentAssertions;
using Pena_e_Arte.Application.Platform.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Platform;

public class SetDunningExclusionHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private SetDunningExclusionHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ExcludedTrue_SetsFlag()
    {
        Guid studioId = Guid.NewGuid();
        await SeedSubscription(studioId);

        await CreateSut().Handle(
            new SetDunningExclusionCommand(studioId, new SetDunningExclusionRequest(true)), default);

        _db.Subscriptions.Single(s => s.StudioId == studioId)
            .DunningExcludedManually.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ExcludedFalse_ClearsFlag()
    {
        Guid studioId = Guid.NewGuid();
        await SeedSubscription(studioId, dunningExcluded: true);

        await CreateSut().Handle(
            new SetDunningExclusionCommand(studioId, new SetDunningExclusionRequest(false)), default);

        _db.Subscriptions.Single(s => s.StudioId == studioId)
            .DunningExcludedManually.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_NoSubscriptionForStudio_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new SetDunningExclusionCommand(Guid.NewGuid(), new SetDunningExclusionRequest(true)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public void Command_ExposesAuditMetadata()
    {
        Guid studioId = Guid.NewGuid();
        SetDunningExclusionCommand command = new(studioId, new SetDunningExclusionRequest(true));

        command.AuditAction.Should().Be("Subscription.DunningExclusionChanged");
        command.AuditTargetType.Should().Be("Subscription");
        command.AuditTargetId.Should().Be(studioId);
        command.AuditStudioId.Should().Be(studioId);
    }

    private async Task SeedSubscription(Guid studioId, bool dunningExcluded = false)
    {
        _db.Subscriptions.Add(new Subscription
        {
            StudioId = studioId,
            Status = SubscriptionStatus.PastDue,
            PastDueSince = DateTime.UtcNow.AddDays(-2),
            DunningExcludedManually = dunningExcluded,
            CurrentPeriodEnd = DateTime.UtcNow.AddDays(-2),
            GracePeriodEnd = DateTime.UtcNow.AddDays(5),
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }
}
