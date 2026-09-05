using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.ConductReports.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.ConductReports;

public class GetMyConductReportsAsArtistHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetMyConductReportsAsArtistHandler CreateSut(ICurrentUser user) => new(_db, user);

    private async Task<Artist> SeedArtistForUser(Guid userId)
    {
        Guid studioId = Guid.NewGuid();
        _db.Studios.Add(new Studio { Id = studioId, Name = "Ink Studio", Slug = "ink-studio", City = "Lisbon", IsActive = true, OwnerEmail = "o@test.com" });
        Artist artist = new() { StudioId = studioId, UserId = userId, FirstName = "Maria", LastName = "Silva", Email = "maria@example.com" };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return artist;
    }

    // The single most important test in this feature: reporter identity must NEVER reach the
    // artist-facing response, no matter what the underlying entity carries.
    [Fact]
    public async Task Handle_NeverExposesReporterIdentity_EvenThoughTheEntityCarriesIt()
    {
        Guid artistUserId = Guid.NewGuid();
        Artist artist = await SeedArtistForUser(artistUserId);

        Guid reporterUserId = Guid.NewGuid();
        ConductReport report = ConductReport.ForArtist(
            artist.StudioId, artist.Id, Guid.NewGuid(), reporterUserId, "Jane Real Name",
            ReportCategory.Harassment, "A real, identifying report body describing the incident.");
        _db.ConductReports.Add(report);
        await _db.SaveChangesAsync();

        FakeCurrentUser user = new(artistUserId, "artist");
        List<ConductReportResponse> result = await CreateSut(user).Handle(new GetMyConductReportsAsArtistQuery(), CancellationToken.None);

        result.Should().ContainSingle();
        result[0].ReporterUserId.Should().BeNull();
        result[0].ReporterName.Should().BeNull();
        // Sanity check the underlying row really does carry the identity (proves this is a
        // real redaction, not an accidental absence of data).
        _db.ConductReports.Single().ReporterUserId.Should().Be(reporterUserId);
        _db.ConductReports.Single().ReporterName.Should().Be("Jane Real Name");
    }

    [Fact]
    public async Task Handle_OnlyReturnsReportsForCallersOwnArtistRecord()
    {
        Guid myUserId = Guid.NewGuid();
        Artist me = await SeedArtistForUser(myUserId);
        Artist otherArtist = await SeedArtistForUser(Guid.NewGuid());

        _db.ConductReports.Add(ConductReport.ForArtist(
            me.StudioId, me.Id, Guid.NewGuid(), Guid.NewGuid(), "Reporter",
            ReportCategory.Other, "A report body about me, long enough to be valid."));
        _db.ConductReports.Add(ConductReport.ForArtist(
            otherArtist.StudioId, otherArtist.Id, Guid.NewGuid(), Guid.NewGuid(), "Reporter",
            ReportCategory.Other, "A report body about someone else, not me at all."));
        await _db.SaveChangesAsync();

        FakeCurrentUser user = new(myUserId, "artist");
        List<ConductReportResponse> result = await CreateSut(user).Handle(new GetMyConductReportsAsArtistQuery(), CancellationToken.None);

        result.Should().ContainSingle(r => r.ArtistId == me.Id);
    }

    [Fact]
    public async Task Handle_NoArtistRecordForUser_ReturnsEmpty()
    {
        FakeCurrentUser user = new(Guid.NewGuid(), "artist");
        List<ConductReportResponse> result = await CreateSut(user).Handle(new GetMyConductReportsAsArtistQuery(), CancellationToken.None);

        result.Should().BeEmpty();
    }
}
