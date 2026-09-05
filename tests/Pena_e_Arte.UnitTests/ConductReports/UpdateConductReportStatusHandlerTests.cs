using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.ConductReports.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ConductReports;

public class UpdateConductReportStatusHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();

    private UpdateConductReportStatusHandler CreateSut(ICurrentUser user) => new(_db, user, _tenant);

    private async Task<ConductReport> SeedReport(Guid studioId, ReportCategory category, Guid? artistId = null)
    {
        ConductReport report = artistId is Guid aid
            ? ConductReport.ForArtist(studioId, aid, Guid.NewGuid(), Guid.NewGuid(), "Reporter", category, "A detailed report body long enough to pass validation.")
            : ConductReport.ForStudio(studioId, Guid.NewGuid(), Guid.NewGuid(), "Reporter", category, "A detailed report body long enough to pass validation.");
        _db.ConductReports.Add(report);
        await _db.SaveChangesAsync();
        return report;
    }

    [Fact]
    public async Task Handle_OwnerResolvingStandardSeverityOwnStudio_Succeeds()
    {
        Guid studioId = Guid.NewGuid();
        _tenant.IsSet.Returns(true);
        _tenant.StudioId.Returns(studioId);
        ConductReport report = await SeedReport(studioId, ReportCategory.PoorServiceQuality);

        FakeCurrentUser owner = FakeCurrentUser.Owner();
        await CreateSut(owner).Handle(
            new UpdateConductReportStatusCommand(report.Id, ReportStatus.Resolved, "Addressed with the artist."),
            CancellationToken.None);

        _db.ConductReports.Single(r => r.Id == report.Id).Status.Should().Be(ReportStatus.Resolved);
    }

    [Fact]
    public async Task Handle_OwnerResolvingHighSeverityOwnStudio_ThrowsForbiddenException()
    {
        Guid studioId = Guid.NewGuid();
        _tenant.IsSet.Returns(true);
        _tenant.StudioId.Returns(studioId);
        ConductReport report = await SeedReport(studioId, ReportCategory.SexualMisconduct);

        FakeCurrentUser owner = FakeCurrentUser.Owner();
        Func<Task> act = () => CreateSut(owner).Handle(
            new UpdateConductReportStatusCommand(report.Id, ReportStatus.Dismissed, "Not valid."),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        _db.ConductReports.Single(r => r.Id == report.Id).Status.Should().Be(ReportStatus.Open);
    }

    [Fact]
    public async Task Handle_IssuerResolvingHighSeverity_Succeeds()
    {
        Guid studioId = Guid.NewGuid();
        _tenant.IsSet.Returns(false);
        ConductReport report = await SeedReport(studioId, ReportCategory.SexualMisconduct);

        FakeCurrentUser issuer = new(Guid.NewGuid(), "issuer");
        await CreateSut(issuer).Handle(
            new UpdateConductReportStatusCommand(report.Id, ReportStatus.Resolved, "Investigated and resolved."),
            CancellationToken.None);

        _db.ConductReports.Single(r => r.Id == report.Id).Status.Should().Be(ReportStatus.Resolved);
    }

    [Fact]
    public async Task Handle_IssuerResolvingStandardSeverity_Succeeds()
    {
        Guid studioId = Guid.NewGuid();
        _tenant.IsSet.Returns(false);
        ConductReport report = await SeedReport(studioId, ReportCategory.Other);

        FakeCurrentUser issuer = new(Guid.NewGuid(), "issuer");
        await CreateSut(issuer).Handle(
            new UpdateConductReportStatusCommand(report.Id, ReportStatus.Dismissed, null),
            CancellationToken.None);

        _db.ConductReports.Single(r => r.Id == report.Id).Status.Should().Be(ReportStatus.Dismissed);
    }

    [Fact]
    public async Task Handle_OwnerOfDifferentStudio_ThrowsForbiddenException()
    {
        Guid reportStudioId = Guid.NewGuid();
        _tenant.IsSet.Returns(true);
        _tenant.StudioId.Returns(Guid.NewGuid()); // a different studio
        ConductReport report = await SeedReport(reportStudioId, ReportCategory.PoorServiceQuality);

        FakeCurrentUser owner = FakeCurrentUser.Owner();
        Func<Task> act = () => CreateSut(owner).Handle(
            new UpdateConductReportStatusCommand(report.Id, ReportStatus.Resolved, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Handle_Artist_ThrowsForbiddenException()
    {
        Guid studioId = Guid.NewGuid();
        Guid artistId = Guid.NewGuid();
        _tenant.IsSet.Returns(true);
        _tenant.StudioId.Returns(studioId);
        ConductReport report = await SeedReport(studioId, ReportCategory.PoorServiceQuality, artistId);

        _db.Artists.Add(new Artist { Id = artistId, StudioId = studioId, FirstName = "A", LastName = "B", Email = "a@b.test", UserId = Guid.NewGuid() });
        await _db.SaveChangesAsync();

        Guid artistUserId = _db.Artists.Single(a => a.Id == artistId).UserId!.Value;
        FakeCurrentUser artistUser = new(artistUserId, "artist");

        Func<Task> act = () => CreateSut(artistUser).Handle(
            new UpdateConductReportStatusCommand(report.Id, ReportStatus.Resolved, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
    }

    [Fact]
    public async Task Handle_ReportNotFound_ThrowsNotFoundException()
    {
        FakeCurrentUser issuer = new(Guid.NewGuid(), "issuer");
        Func<Task> act = () => CreateSut(issuer).Handle(
            new UpdateConductReportStatusCommand(Guid.NewGuid(), ReportStatus.Resolved, null),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
