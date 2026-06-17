using FluentAssertions;
using MediatR;
using NSubstitute;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class ReviewDesignHandlerTests
{
    private readonly FakeDbContext     _db       = FakeDbContext.Create();
    private readonly ICurrentTenant    _tenant   = Substitute.For<ICurrentTenant>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly ISender           _sender   = Substitute.For<ISender>();
    private readonly Guid              _studioId = Guid.NewGuid();

    public ReviewDesignHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private ReviewDesignHandler CreateSut() => new(_db, _tenant, _realtime, _sender);

    [Fact]
    public async Task Handle_ApproveRevisionWithNoExistingApproval_CreatesApprovalWithApprovedStatus()
    {
        Guid revisionId = await SeedRevision(approval: null);

        await CreateSut()
            .Handle(new ReviewDesignCommand(new ReviewDesignRequest(revisionId, Approved: true, Notes: null)), default);

        _db.DesignApprovals.Should().ContainSingle(a =>
            a.DesignRevisionId == revisionId &&
            a.Status           == DesignApprovalStatus.Approved);
    }

    [Fact]
    public async Task Handle_RequestChangesWithNoExistingApproval_CreatesApprovalWithChangesRequestedStatus()
    {
        Guid revisionId = await SeedRevision(approval: null);

        await CreateSut()
            .Handle(new ReviewDesignCommand(new ReviewDesignRequest(revisionId, Approved: false, Notes: "Fix the shading")), default);

        _db.DesignApprovals.Should().ContainSingle(a =>
            a.DesignRevisionId == revisionId &&
            a.Status           == DesignApprovalStatus.ChangesRequested &&
            a.ClientNotes      == "Fix the shading");
    }

    [Fact]
    public async Task Handle_ApproveRevision_NotifiesRealtimeWithDesignApproved()
    {
        Guid revisionId = await SeedRevision(approval: null);

        await CreateSut()
            .Handle(new ReviewDesignCommand(new ReviewDesignRequest(revisionId, Approved: true, Notes: null)), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(_studioId, "DesignApproved", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RequestChanges_NotifiesRealtimeWithDesignChangeRequested()
    {
        Guid revisionId = await SeedRevision(approval: null);

        await CreateSut()
            .Handle(new ReviewDesignCommand(new ReviewDesignRequest(revisionId, Approved: false, Notes: null)), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(_studioId, "DesignChangeRequested", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ExistingChangesRequestedApproval_UpdatesStatusToApproved()
    {
        Guid revisionId = await SeedRevision(approval: DesignApprovalStatus.ChangesRequested);
        _db.ChangeTracker.Clear();

        await CreateSut()
            .Handle(new ReviewDesignCommand(new ReviewDesignRequest(revisionId, Approved: true, Notes: null)), default);

        _db.DesignApprovals.Should().ContainSingle(a =>
            a.DesignRevisionId == revisionId &&
            a.Status           == DesignApprovalStatus.Approved);
    }

    [Fact]
    public async Task Handle_ExistingApproval_DoesNotCreateDuplicate()
    {
        Guid revisionId = await SeedRevision(approval: DesignApprovalStatus.ChangesRequested);
        _db.ChangeTracker.Clear();

        await CreateSut()
            .Handle(new ReviewDesignCommand(new ReviewDesignRequest(revisionId, Approved: true, Notes: null)), default);

        _db.DesignApprovals.Where(a => a.DesignRevisionId == revisionId)
            .Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_AlreadyApprovedRevision_ThrowsDesignAlreadyApprovedException()
    {
        Guid revisionId = await SeedRevision(approval: DesignApprovalStatus.Approved);
        _db.ChangeTracker.Clear();

        Func<Task> act = () => CreateSut()
            .Handle(new ReviewDesignCommand(new ReviewDesignRequest(revisionId, Approved: true, Notes: null)), default);

        await act.Should().ThrowAsync<DesignAlreadyApprovedException>();
    }

    [Fact]
    public async Task Handle_RevisionNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut()
            .Handle(new ReviewDesignCommand(new ReviewDesignRequest(Guid.NewGuid(), Approved: true, Notes: null)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> SeedRevision(DesignApprovalStatus? approval)
    {
        DesignRevision revision = new()
        {
            StudioId      = _studioId,
            DesignId      = Guid.NewGuid(),
            VersionNumber = 1,
            FileUrl       = "https://r2.example.com/v1.png",
            UploadedAt    = DateTime.UtcNow
        };

        if (approval.HasValue)
        {
            DesignApproval designApproval = new()
            {
                StudioId         = _studioId,
                DesignRevisionId = revision.Id,
                Status           = approval.Value,
                ReviewedAt       = DateTime.UtcNow
            };
            revision.Approval = designApproval;
        }

        _db.DesignRevisions.Add(revision);
        await _db.SaveChangesAsync();
        return revision.Id;
    }
}
