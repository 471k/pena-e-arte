using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Designs.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class GetDesignRevisionsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IR2Service _r2 = Substitute.For<IR2Service>();
    private readonly Guid _studioId = Guid.NewGuid();

    private GetDesignRevisionsHandler CreateSut(FakeCurrentUser? user = null) => new(_db, _r2, user ?? FakeCurrentUser.Owner());

    // The read now 404s for a design that doesn't exist, so every seeded revision gets its design.
    private Guid EnsureDesign(Guid designId, Guid? clientId = null, Guid? artistId = null)
    {
        if (!_db.Designs.Any(d => d.Id == designId))
        {
            _db.Designs.Add(new Design
            {
                Id = designId,
                StudioId = _studioId,
                Title = "D",
                ClientId = clientId ?? Guid.NewGuid(),
                ArtistId = artistId ?? Guid.NewGuid(),
            });
            _db.SaveChanges();
        }
        return designId;
    }

    [Fact]
    public async Task Handle_RevisionsExist_ReturnsInVersionOrder()
    {
        Guid designId = Guid.NewGuid();
        await SeedRevision(designId, version: 2);
        await SeedRevision(designId, version: 1);

        List<DesignRevisionResponse> result = await CreateSut()
            .Handle(new GetDesignRevisionsQuery(designId), default);

        result.Should().HaveCount(2);
        result[0].VersionNumber.Should().Be(1);
        result[1].VersionNumber.Should().Be(2);
    }

    [Fact]
    public async Task Handle_NoRevisions_ReturnsEmpty()
    {
        List<DesignRevisionResponse> result = await CreateSut()
            .Handle(new GetDesignRevisionsQuery(EnsureDesign(Guid.NewGuid())), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_RevisionWithNoApproval_ReturnsNullApprovalStatus()
    {
        Guid designId = Guid.NewGuid();
        Guid revisionId = await SeedRevision(designId, version: 1, approval: null);

        List<DesignRevisionResponse> result = await CreateSut()
            .Handle(new GetDesignRevisionsQuery(designId), default);

        result.Single().ApprovalStatus.Should().BeNull();
    }

    [Fact]
    public async Task Handle_ApprovedRevision_ReturnsApprovalStatus()
    {
        Guid designId = Guid.NewGuid();
        await SeedRevision(designId, version: 1, approval: DesignApprovalStatus.Approved);

        List<DesignRevisionResponse> result = await CreateSut()
            .Handle(new GetDesignRevisionsQuery(designId), default);

        result.Single().ApprovalStatus.Should().Be("Approved");
    }

    [Fact]
    public async Task Handle_ChangesRequestedRevision_ReturnsChangesRequestedStatus()
    {
        Guid designId = Guid.NewGuid();
        await SeedRevision(designId, version: 1, approval: DesignApprovalStatus.ChangesRequested,
            approvalNotes: "Fix the outline");

        List<DesignRevisionResponse> result = await CreateSut()
            .Handle(new GetDesignRevisionsQuery(designId), default);

        result.Single().ApprovalStatus.Should().Be("ChangesRequested");
        result.Single().ApprovalNotes.Should().Be("Fix the outline");
    }

    [Fact]
    public async Task Handle_OnlyReturnsRevisionsForGivenDesign()
    {
        Guid designA = Guid.NewGuid();
        Guid designB = Guid.NewGuid();
        await SeedRevision(designA, version: 1);
        await SeedRevision(designB, version: 1);

        List<DesignRevisionResponse> result = await CreateSut()
            .Handle(new GetDesignRevisionsQuery(designA), default);

        result.Should().ContainSingle(r => r.DesignId == designA);
    }

    [Fact]
    public async Task Handle_CarriesWhenEachRevisionWasReviewed_AsHistory()
    {
        // E04c (A05): per-revision history is a read of the existing approval row.
        Guid designId = Guid.NewGuid();
        await SeedRevision(designId, version: 1, approval: DesignApprovalStatus.ChangesRequested, approvalNotes: "Thinner lines");
        await SeedRevision(designId, version: 2);

        List<DesignRevisionResponse> result = await CreateSut()
            .Handle(new GetDesignRevisionsQuery(designId), default);

        result[0].ReviewedAt.Should().NotBeNull();
        result[0].ApprovalNotes.Should().Be("Thinner lines");
        result[1].ReviewedAt.Should().BeNull("v2 has never been reviewed");
    }

    private async Task<Guid> SeedRevision(
        Guid designId,
        int version,
        DesignApprovalStatus? approval = null,
        string? approvalNotes = null)
    {
        EnsureDesign(designId);
        DesignRevision revision = new()
        {
            StudioId = _studioId,
            DesignId = designId,
            VersionNumber = version,
            FileUrl = $"https://r2.example.com/v{version}.png",
            UploadedAt = DateTime.UtcNow,
        };

        if (approval.HasValue)
        {
            revision.Approval = new DesignApproval
            {
                StudioId = _studioId,
                DesignRevisionId = revision.Id,
                Status = approval.Value,
                ClientNotes = approvalNotes,
                ReviewedAt = DateTime.UtcNow,
            };
        }

        _db.DesignRevisions.Add(revision);
        await _db.SaveChangesAsync();
        return revision.Id;
    }
}
