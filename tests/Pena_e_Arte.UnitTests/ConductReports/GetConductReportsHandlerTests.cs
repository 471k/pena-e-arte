using FluentAssertions;
using Pena_e_Arte.Application.ConductReports.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ConductReports;

public class GetConductReportsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetConductReportsHandler CreateSut() => new(_db);

    private void SeedStudio(Guid studioId) =>
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Studio", Slug = $"studio-{studioId}", City = "Lisbon", IsActive = true, OwnerEmail = "o@test.com" });

    [Fact]
    public async Task Handle_ReturnsReportsAcrossAllStudios_WithFullReporterIdentity()
    {
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();
        SeedStudio(studioA);
        SeedStudio(studioB);

        Guid reporterUserId = Guid.NewGuid();
        _db.ConductReports.Add(ConductReport.ForStudio(
            studioA, Guid.NewGuid(), reporterUserId, "Jane Doe", ReportCategory.Scam,
            "A cross-tenant admin read should see this report from studio A."));
        _db.ConductReports.Add(ConductReport.ForStudio(
            studioB, Guid.NewGuid(), Guid.NewGuid(), "John Roe", ReportCategory.Other,
            "A cross-tenant admin read should also see this report from studio B."));
        await _db.SaveChangesAsync();

        List<ConductReportResponse> result = await CreateSut().Handle(new GetConductReportsQuery(), CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().Contain(r => r.ReporterUserId == reporterUserId && r.ReporterName == "Jane Doe");
    }

    [Fact]
    public async Task Handle_FiltersByCategoryAndStudioId()
    {
        Guid studioA = Guid.NewGuid();
        Guid studioB = Guid.NewGuid();
        SeedStudio(studioA);
        SeedStudio(studioB);

        ConductReport match = ConductReport.ForStudio(
            studioA, Guid.NewGuid(), Guid.NewGuid(), "A", ReportCategory.SexualMisconduct,
            "This is the one matching both filters in this test.");
        ConductReport wrongCategory = ConductReport.ForStudio(
            studioA, Guid.NewGuid(), Guid.NewGuid(), "B", ReportCategory.Other,
            "This has the right studio but the wrong category value.");
        ConductReport wrongStudio = ConductReport.ForStudio(
            studioB, Guid.NewGuid(), Guid.NewGuid(), "C", ReportCategory.SexualMisconduct,
            "This has the right category but the wrong studio value.");
        _db.ConductReports.AddRange(match, wrongCategory, wrongStudio);
        await _db.SaveChangesAsync();

        List<ConductReportResponse> result = await CreateSut().Handle(
            new GetConductReportsQuery(Category: "SexualMisconduct", StudioId: studioA), CancellationToken.None);

        result.Should().ContainSingle(r => r.Id == match.Id);
    }
}
