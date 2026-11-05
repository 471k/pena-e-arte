using FluentAssertions;
using Pena_e_Arte.Application.Designs;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.UnitTests.Designs;

/// <summary>
/// E04c (A05): whose move it is on a design, derived from the revision and approval rows that already
/// exist. Only the latest revision decides; a review of an older one is history.
/// </summary>
public class DesignReviewStateTests
{
    private static DesignRevision Revision(int version, DesignApprovalStatus? approval) => new()
    {
        VersionNumber = version,
        UploadedAt = new DateTime(2026, 11, version, 10, 0, 0, DateTimeKind.Utc),
        Approval = approval is { } status ? new DesignApproval { Status = status } : null,
    };

    private static Design Design(params DesignRevision[] revisions) => new()
    {
        ClientId = Guid.NewGuid(),
        Title = "Sleeve",
        Revisions = revisions.ToList(),
    };

    [Fact]
    public void NoRevisions_IsADraft_WaitingOnTheArtist()
    {
        DesignProgress p = DesignReviewState.Derive(Design());

        p.Status.Should().Be("Draft");
        p.WaitingOn.Should().Be(DesignWaitingOn.Artist);
        p.Latest.Should().BeNull();
    }

    [Fact]
    public void AnUnreviewedLatestRevision_WaitsOnTheClient()
    {
        DesignProgress p = DesignReviewState.Derive(Design(Revision(1, null)));

        p.Status.Should().Be("InReview");
        p.WaitingOn.Should().Be(DesignWaitingOn.Client);
        p.Latest!.VersionNumber.Should().Be(1);
    }

    [Fact]
    public void APendingApprovalRow_StillWaitsOnTheClient()
    {
        DesignReviewState.Derive(Design(Revision(1, DesignApprovalStatus.Pending)))
            .WaitingOn.Should().Be(DesignWaitingOn.Client);
    }

    [Fact]
    public void ChangesRequestedOnTheLatest_WaitsOnTheArtist()
    {
        DesignProgress p = DesignReviewState.Derive(Design(Revision(1, DesignApprovalStatus.ChangesRequested)));

        p.Status.Should().Be("ChangesRequested");
        p.WaitingOn.Should().Be(DesignWaitingOn.Artist);
    }

    [Fact]
    public void AnExpiredReviewWindow_WaitsOnTheArtist_LikeChangesRequested()
    {
        DesignProgress p = DesignReviewState.Derive(Design(Revision(1, DesignApprovalStatus.Expired)));

        p.Status.Should().Be("ChangesRequested");
        p.WaitingOn.Should().Be(DesignWaitingOn.Artist);
    }

    [Fact]
    public void AnApprovedLatest_WaitsOnNobody()
    {
        DesignProgress p = DesignReviewState.Derive(Design(Revision(1, DesignApprovalStatus.ChangesRequested), Revision(2, DesignApprovalStatus.Approved)));

        p.Status.Should().Be("Approved");
        p.WaitingOn.Should().Be(DesignWaitingOn.Nobody);
        p.Latest!.VersionNumber.Should().Be(2);
    }

    [Fact]
    public void AnUnreviewedLatest_AfterAnApprovedEarlierRevision_WaitsOnTheClient()
    {
        // v1 approved, then the artist uploaded v2: the client now owes a review of v2. The earlier
        // approval is history and does not make the design "Approved".
        DesignProgress p = DesignReviewState.Derive(Design(Revision(1, DesignApprovalStatus.Approved), Revision(2, null)));

        p.Status.Should().Be("InReview");
        p.WaitingOn.Should().Be(DesignWaitingOn.Client);
        p.Latest!.VersionNumber.Should().Be(2);
    }

    [Fact]
    public void TheLatestIsTheHighestVersion_NotTheLastInTheList()
    {
        DesignProgress p = DesignReviewState.Derive(Design(Revision(3, null), Revision(1, DesignApprovalStatus.Approved), Revision(2, DesignApprovalStatus.ChangesRequested)));

        p.Latest!.VersionNumber.Should().Be(3);
        p.WaitingOn.Should().Be(DesignWaitingOn.Client);
    }

    [Fact]
    public void ADeletedRevision_DoesNotCountAsTheLatest()
    {
        DesignRevision deleted = Revision(2, null);
        deleted.DeletedAt = DateTime.UtcNow;

        DesignProgress p = DesignReviewState.Derive(Design(Revision(1, DesignApprovalStatus.ChangesRequested), deleted));

        p.Latest!.VersionNumber.Should().Be(1);
        p.WaitingOn.Should().Be(DesignWaitingOn.Artist);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(DesignApprovalStatus.ChangesRequested)]
    public void ACatalogTemplate_WaitsOnNobody(DesignApprovalStatus? approval)
    {
        Design catalog = Design(Revision(1, approval));
        catalog.ClientId = null;
        catalog.IsCatalogItem = true;

        DesignReviewState.Derive(catalog).WaitingOn.Should().Be(DesignWaitingOn.Nobody);
        DesignReviewState.Derive(new Design { IsCatalogItem = true }).WaitingOn.Should().Be(DesignWaitingOn.Nobody);
    }
}
