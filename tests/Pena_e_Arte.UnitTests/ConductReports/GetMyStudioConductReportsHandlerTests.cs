using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.ConductReports.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ConductReports;

public class GetMyStudioConductReportsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();

    private GetMyStudioConductReportsHandler CreateSut() => new(_db, _tenant);

    private async Task SeedStudio(Guid studioId) =>
        await Task.Run(() => _db.Studios.Add(new Studio
        {
            Id = studioId,
            Name = "Ink Studio",
            Slug = $"studio-{studioId}",
            City = "Lisbon",
            IsActive = true,
            OwnerEmail = "o@test.com",
        }));

    [Fact]
    public async Task Handle_OnlyReturnsOwnStudioReports_WithFullReporterIdentity()
    {
        Guid studioId = Guid.NewGuid();
        Guid otherStudioId = Guid.NewGuid();
        await SeedStudio(studioId);
        await SeedStudio(otherStudioId);
        _tenant.StudioId.Returns(studioId);

        Guid reporterUserId = Guid.NewGuid();
        _db.ConductReports.Add(ConductReport.ForStudio(
            studioId, Guid.NewGuid(), reporterUserId, "Jane Doe", ReportCategory.PoorServiceQuality,
            "A report body long enough to be considered valid for this test."));
        _db.ConductReports.Add(ConductReport.ForStudio(
            otherStudioId, Guid.NewGuid(), Guid.NewGuid(), "Someone Else", ReportCategory.Other,
            "A report about a completely different studio, not this one."));
        await _db.SaveChangesAsync();

        List<ConductReportResponse> result = await CreateSut().Handle(new GetMyStudioConductReportsQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].ReporterUserId.Should().Be(reporterUserId);
        result[0].ReporterName.Should().Be("Jane Doe");
    }

    [Fact]
    public async Task Handle_FiltersByStatus()
    {
        Guid studioId = Guid.NewGuid();
        await SeedStudio(studioId);
        _tenant.StudioId.Returns(studioId);

        ConductReport open = ConductReport.ForStudio(
            studioId, Guid.NewGuid(), Guid.NewGuid(), "A", ReportCategory.Other, "Report body number one for this test case.");
        ConductReport resolved = ConductReport.ForStudio(
            studioId, Guid.NewGuid(), Guid.NewGuid(), "B", ReportCategory.Other, "Report body number two for this test case.");
        resolved.UpdateStatus(ReportStatus.Resolved, "Done.");
        _db.ConductReports.AddRange(open, resolved);
        await _db.SaveChangesAsync();

        List<ConductReportResponse> result = await CreateSut().Handle(
            new GetMyStudioConductReportsQuery(Status: "Resolved"), CancellationToken.None);

        result.Should().ContainSingle(r => r.Id == resolved.Id);
    }
}
