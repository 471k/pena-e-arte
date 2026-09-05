using FluentAssertions;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.UnitTests.ConductReports;

public class ConductReportEntityTests
{
    private static ConductReport ArtistTargetReport(Guid studioId, Guid artistId) =>
        ConductReport.ForArtist(
            studioId, artistId, Guid.NewGuid(), Guid.NewGuid(), "Some Client",
            ReportCategory.Harassment, "This artist was verbally abusive during my session.");

    private static ConductReport StudioTargetReport(Guid studioId) =>
        ConductReport.ForStudio(
            studioId, Guid.NewGuid(), Guid.NewGuid(), "Some Client",
            ReportCategory.UnsafeHygienePractices, "The studio did not sterilize equipment properly.");

    [Fact]
    public void IsReadableBy_Issuer_AlwaysTrue()
    {
        ConductReport report = ArtistTargetReport(Guid.NewGuid(), Guid.NewGuid());

        report.IsReadableBy(callerStudioId: null, callerArtistId: null, role: "issuer").Should().BeTrue();
    }

    [Fact]
    public void IsReadableBy_OwnerOfOwnStudio_True()
    {
        Guid studioId = Guid.NewGuid();
        ConductReport report = ArtistTargetReport(studioId, Guid.NewGuid());

        report.IsReadableBy(callerStudioId: studioId, callerArtistId: null, role: "owner").Should().BeTrue();
    }

    [Fact]
    public void IsReadableBy_OwnerOfDifferentStudio_False()
    {
        ConductReport report = ArtistTargetReport(Guid.NewGuid(), Guid.NewGuid());

        report.IsReadableBy(callerStudioId: Guid.NewGuid(), callerArtistId: null, role: "owner").Should().BeFalse();
    }

    [Fact]
    public void IsReadableBy_ArtistWhoIsTheTarget_True()
    {
        Guid artistId = Guid.NewGuid();
        ConductReport report = ArtistTargetReport(Guid.NewGuid(), artistId);

        report.IsReadableBy(callerStudioId: null, callerArtistId: artistId, role: "artist").Should().BeTrue();
    }

    [Fact]
    public void IsReadableBy_DifferentArtist_False()
    {
        ConductReport report = ArtistTargetReport(Guid.NewGuid(), Guid.NewGuid());

        report.IsReadableBy(callerStudioId: null, callerArtistId: Guid.NewGuid(), role: "artist").Should().BeFalse();
    }

    [Fact]
    public void IsReadableBy_ArtistOnStudioTargetReport_False()
    {
        // Studio-target reports have ArtistId == null — an artist must never match one,
        // regardless of which artist id they carry.
        ConductReport report = StudioTargetReport(Guid.NewGuid());

        report.IsReadableBy(callerStudioId: null, callerArtistId: Guid.NewGuid(), role: "artist").Should().BeFalse();
    }

    [Fact]
    public void IsReadableBy_Client_AlwaysFalse()
    {
        // Decision: the reporting client never gets a persistent read of their own filed
        // reports — there is no client branch in IsReadableBy at all.
        Guid studioId = Guid.NewGuid();
        ConductReport report = StudioTargetReport(studioId);

        report.IsReadableBy(callerStudioId: studioId, callerArtistId: null, role: "client").Should().BeFalse();
    }

    [Fact]
    public void UpdateStatus_ToResolved_SetsResolvedAt()
    {
        ConductReport report = StudioTargetReport(Guid.NewGuid());

        report.UpdateStatus(ReportStatus.Resolved, "Handled.");

        report.Status.Should().Be(ReportStatus.Resolved);
        report.ResolutionNote.Should().Be("Handled.");
        report.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public void UpdateStatus_ToReviewing_ClearsResolvedAt()
    {
        ConductReport report = StudioTargetReport(Guid.NewGuid());
        report.UpdateStatus(ReportStatus.Resolved, "Handled.");

        report.UpdateStatus(ReportStatus.Reviewing, null);

        report.Status.Should().Be(ReportStatus.Reviewing);
        report.ResolvedAt.Should().BeNull();
    }
}
