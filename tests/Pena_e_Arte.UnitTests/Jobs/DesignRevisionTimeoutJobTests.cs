using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Jobs;

public class DesignRevisionTimeoutJobTests
{
    private readonly FakeDbContext     _db       = FakeDbContext.Create();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly Guid              _studioId = Guid.NewGuid();

    private DesignRevisionTimeoutJob CreateSut() => new(_db, _realtime);

    [Fact]
    public async Task ExecuteAsync_NoPriorApproval_CreatesExpiredApproval()
    {
        Guid revisionId = await SeedRevision();

        await CreateSut().ExecuteAsync(revisionId);

        _db.DesignApprovals.Should().ContainSingle(a =>
            a.DesignRevisionId == revisionId &&
            a.Status == DesignApprovalStatus.Expired);
    }

    [Fact]
    public async Task ExecuteAsync_PendingApproval_UpdatesToExpired()
    {
        Guid revisionId = await SeedRevision();
        await SeedApproval(revisionId, DesignApprovalStatus.Pending);

        await CreateSut().ExecuteAsync(revisionId);

        _db.DesignApprovals.Single(a => a.DesignRevisionId == revisionId)
            .Status.Should().Be(DesignApprovalStatus.Expired);
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyApproved_DoesNotOverwrite()
    {
        Guid revisionId = await SeedRevision();
        await SeedApproval(revisionId, DesignApprovalStatus.Approved);

        await CreateSut().ExecuteAsync(revisionId);

        _db.DesignApprovals.Single(a => a.DesignRevisionId == revisionId)
            .Status.Should().Be(DesignApprovalStatus.Approved);
    }

    [Fact]
    public async Task ExecuteAsync_ChangesRequested_DoesNotOverwrite()
    {
        Guid revisionId = await SeedRevision();
        await SeedApproval(revisionId, DesignApprovalStatus.ChangesRequested);

        await CreateSut().ExecuteAsync(revisionId);

        _db.DesignApprovals.Single(a => a.DesignRevisionId == revisionId)
            .Status.Should().Be(DesignApprovalStatus.ChangesRequested);
    }

    [Fact]
    public async Task ExecuteAsync_RevisionNotFound_DoesNotThrow()
    {
        Func<Task> act = () => CreateSut().ExecuteAsync(Guid.NewGuid());

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_Expires_NotifiesDesignRevisionExpired()
    {
        Guid revisionId = await SeedRevision();

        await CreateSut().ExecuteAsync(revisionId);

        await _realtime.Received(1)
            .NotifyStudioAsync(_studioId, "DesignRevisionExpired", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_AlreadyApproved_DoesNotNotify()
    {
        Guid revisionId = await SeedRevision();
        await SeedApproval(revisionId, DesignApprovalStatus.Approved);

        await CreateSut().ExecuteAsync(revisionId);

        await _realtime.DidNotReceive()
            .NotifyStudioAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    private async Task<Guid> SeedRevision()
    {
        Design design = new() { StudioId = _studioId, ClientId = Guid.NewGuid(), ArtistId = Guid.NewGuid(), Title = "Test" };
        _db.Designs.Add(design);

        DesignRevision revision = new()
        {
            StudioId      = _studioId,
            DesignId      = design.Id,
            VersionNumber = 1,
            FileUrl       = "https://r2.example.com/v1.png",
            UploadedAt    = DateTime.UtcNow.AddDays(-15),
        };
        _db.DesignRevisions.Add(revision);
        await _db.SaveChangesAsync();
        return revision.Id;
    }

    private async Task SeedApproval(Guid revisionId, DesignApprovalStatus status)
    {
        _db.DesignApprovals.Add(new DesignApproval
        {
            StudioId         = _studioId,
            DesignRevisionId = revisionId,
            Status           = status,
        });
        await _db.SaveChangesAsync();
    }
}
