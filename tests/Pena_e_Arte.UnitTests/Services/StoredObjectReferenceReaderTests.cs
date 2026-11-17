using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Services;

/// <summary>
/// One test per reference source of the Phase 0 table (11 columns). If a source is dropped from
/// StoredObjectReferenceReader, its test fails: the sweep would otherwise treat that column's objects as
/// orphans and delete live files. FakeDbContext applies no query filters, so the tenant and soft-delete
/// behaviour under the real filters is proved in StoredObjectReferenceReaderIntegrationTests instead.
/// </summary>
public class StoredObjectReferenceReaderTests
{
    private const string Address = "https://media.example.com";
    private const string OldAddress = "https://bucket.old-worker.example.dev";

    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private readonly R2Service _r2 = new(Substitute.For<IAmazonS3>(), Options.Create(new R2Options
    {
        BucketName = "test-bucket",
        PublicUrl = Address,
        LegacyPublicUrls = [OldAddress],
    }));

    private Task<StoredObjectReferences> Read() => new StoredObjectReferenceReader(_db, _r2).ReadAsync(default);

    private string Key(string area) => $"{_studioId}/{area}/{Guid.NewGuid()}.jpg";

    private static string Url(string key) => $"{Address}/{key}";

    [Fact]
    public async Task Source1_PortfolioImage()
    {
        string key = Key("portfolio");
        _db.PortfolioImages.Add(new PortfolioImage { StudioId = _studioId, ArtistId = Guid.NewGuid(), ImageUrl = Url(key) });
        await _db.SaveChangesAsync();

        (await Read()).IsReferenced(key).Should().BeTrue();
    }

    [Fact]
    public async Task Source2_TattooRecord_EveryPhotoInTheList()
    {
        string first = Key("clients/c/photos");
        string second = Key("clients/c/photos");
        _db.TattooRecords.Add(new TattooRecord { StudioId = _studioId, ClientId = Guid.NewGuid(), PhotoUrls = [Url(first), Url(second)] });
        await _db.SaveChangesAsync();

        StoredObjectReferences refs = await Read();

        refs.IsReferenced(first).Should().BeTrue();
        refs.IsReferenced(second).Should().BeTrue();
    }

    [Fact]
    public async Task Source3_DesignRevision()
    {
        string key = Key("designs/d");
        _db.DesignRevisions.Add(new DesignRevision { StudioId = _studioId, DesignId = Guid.NewGuid(), FileUrl = Url(key) });
        await _db.SaveChangesAsync();

        (await Read()).IsReferenced(key).Should().BeTrue();
    }

    [Fact]
    public async Task Source4_AppointmentAttachment()
    {
        string key = Key("appointments/pending/s/Reference");
        _db.AppointmentAttachments.Add(new AppointmentAttachment { StudioId = _studioId, AppointmentId = Guid.NewGuid(), ImageUrl = Url(key) });
        await _db.SaveChangesAsync();

        (await Read()).IsReferenced(key).Should().BeTrue();
    }

    [Fact]
    public async Task Source5_ConductReport_EveryAttachment()
    {
        string key = Key("conduct-reports/pending/s");
        _db.ConductReports.Add(ConductReport.ForStudio(_studioId, Guid.NewGuid(), Guid.NewGuid(), "R", ReportCategory.Other, "why", [Url(key)]));
        await _db.SaveChangesAsync();

        (await Read()).IsReferenced(key).Should().BeTrue();
    }

    [Fact]
    public async Task Source6_FeedbackReport_EveryAttachment()
    {
        string key = Key("feedback/pending/s");
        _db.FeedbackReports.Add(FeedbackReport.Create(_studioId, Guid.NewGuid(), "owner", "S", FeedbackType.BugReport, "t", "b", [Url(key)]));
        await _db.SaveChangesAsync();

        (await Read()).IsReferenced(key).Should().BeTrue();
    }

    [Fact]
    public async Task Source7_IntakeForm()
    {
        string key = Key("portfolio");
        _db.IntakeForms.Add(new IntakeForm { StudioId = _studioId, ClientId = Guid.NewGuid(), FileUrl = Url(key) });
        await _db.SaveChangesAsync();

        (await Read()).IsReferenced(key).Should().BeTrue();
    }

    [Fact]
    public async Task Source8_ConsentForm()
    {
        string key = $"consent/{_studioId}/{Guid.NewGuid()}.pdf";
        _db.ConsentForms.Add(new ConsentForm { StudioId = _studioId, ClientId = Guid.NewGuid(), SignatureData = "s", FileUrl = Url(key) });
        await _db.SaveChangesAsync();

        (await Read()).IsReferenced(key).Should().BeTrue();
    }

    [Fact]
    public async Task Source9_ArtistProfileImage()
    {
        string key = Key("artists");
        _db.Artists.Add(new Artist { StudioId = _studioId, FirstName = "A", LastName = "B", Email = "a@b.test", ProfileImageUrl = Url(key) });
        await _db.SaveChangesAsync();

        (await Read()).IsReferenced(key).Should().BeTrue();
    }

    [Fact]
    public async Task Source10_ArtistAvatar()
    {
        string key = Key("artists");
        _db.Artists.Add(new Artist { StudioId = _studioId, FirstName = "A", LastName = "B", Email = "a@b.test", AvatarUrl = Url(key) });
        await _db.SaveChangesAsync();

        (await Read()).IsReferenced(key).Should().BeTrue();
    }

    [Fact]
    public async Task Source11_StudioCoverImage()
    {
        string key = Key("cover");
        _db.Studios.Add(new Studio { Id = _studioId, Name = "S", Slug = $"s-{Guid.NewGuid():N}", OwnerEmail = "o@s.test", CoverImageUrl = Url(key) });
        await _db.SaveChangesAsync();

        (await Read()).IsReferenced(key).Should().BeTrue();
    }

    [Fact]
    public async Task SoftDeletedRow_StillProtectsItsObject()
    {
        string key = Key("clients/c/photos");
        _db.TattooRecords.Add(new TattooRecord
        {
            StudioId = _studioId,
            ClientId = Guid.NewGuid(),
            PhotoUrls = [Url(key)],
            DeletedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();

        (await Read()).IsReferenced(key).Should().BeTrue();
    }

    [Fact]
    public async Task UnreferencedKey_IsNotReferenced()
    {
        string stored = Key("portfolio");
        _db.PortfolioImages.Add(new PortfolioImage { StudioId = _studioId, ArtistId = Guid.NewGuid(), ImageUrl = Url(stored) });
        await _db.SaveChangesAsync();

        (await Read()).IsReferenced(Key("portfolio")).Should().BeFalse();
    }

    [Fact]
    public async Task OldAddress_ProtectsTheSameKey()
    {
        string key = Key("portfolio");
        _db.PortfolioImages.Add(new PortfolioImage { StudioId = _studioId, ArtistId = Guid.NewGuid(), ImageUrl = $"{OldAddress}/{key}" });
        await _db.SaveChangesAsync();

        (await Read()).IsReferenced(key).Should().BeTrue();
    }

    // An address nobody recorded in LegacyPublicUrls must still protect its object: deleting a live file
    // because its row holds an unfamiliar host is the failure this whole feature exists to avoid.
    [Fact]
    public async Task UnknownAddress_StillProtectsThePathAsAKey_AndIsCounted()
    {
        string key = Key("portfolio");
        _db.PortfolioImages.Add(new PortfolioImage { StudioId = _studioId, ArtistId = Guid.NewGuid(), ImageUrl = $"https://unrecorded.example.net/{key}?v=2" });
        await _db.SaveChangesAsync();

        StoredObjectReferences refs = await Read();

        refs.IsReferenced(key).Should().BeTrue();
        refs.ValuesOnNoKnownAddress.Should().Be(1);
    }

    [Fact]
    public async Task ExternalPortfolioLink_IsToleratedNotFailed()
    {
        _db.PortfolioImages.Add(new PortfolioImage { StudioId = _studioId, ArtistId = Guid.NewGuid(), ImageUrl = "https://instagram.example/p/abc" });
        _db.PortfolioImages.Add(new PortfolioImage { StudioId = _studioId, ArtistId = Guid.NewGuid(), ImageUrl = "" });
        await _db.SaveChangesAsync();

        StoredObjectReferences refs = await Read();

        refs.ValuesRead.Should().Be(1);
        refs.ValuesOnNoKnownAddress.Should().Be(1);
    }

    [Fact]
    public async Task OneObjectInThreeRows_IsReferencedWhileAnyRowRemains()
    {
        // The catalog case (RequestCatalogDesignCommand): one designs/ object in the source revision, the
        // client's cloned revision and an appointment attachment.
        string key = Key("designs/d");
        DesignRevision source = new() { StudioId = _studioId, DesignId = Guid.NewGuid(), FileUrl = Url(key) };
        DesignRevision clone = new() { StudioId = _studioId, DesignId = Guid.NewGuid(), FileUrl = Url(key) };
        _db.DesignRevisions.AddRange(source, clone);
        _db.AppointmentAttachments.Add(new AppointmentAttachment { StudioId = _studioId, AppointmentId = Guid.NewGuid(), ImageUrl = Url(key) });
        await _db.SaveChangesAsync();

        _db.DesignRevisions.RemoveRange(source, clone);
        await _db.SaveChangesAsync();

        (await Read()).IsReferenced(key).Should().BeTrue();
    }

    [Fact]
    public async Task ASourceThatCannotBeRead_Throws_NeverReportsUnreferenced()
    {
        IAppDbContext broken = Substitute.For<IAppDbContext>();
        broken.PortfolioImages.Returns(_db.PortfolioImages);
        broken.TattooRecords.Returns(_ => throw new InvalidOperationException("database unreachable"));

        Func<Task> act = () => new StoredObjectReferenceReader(broken, _r2).ReadAsync(default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData("https://media.example.com/s1/portfolio/a.jpg", "s1/portfolio/a.jpg")]
    [InlineData("https://MEDIA.example.com/s1/portfolio/a.jpg", "s1/portfolio/a.jpg")]
    [InlineData("https://other.example:8443/s1/a.jpg#x", "s1/a.jpg")]
    [InlineData("s1/portfolio/a.jpg", "s1/portfolio/a.jpg")]
    public void CandidateKeys_IncludeTheKeyBehindTheAddress(string value, string expectedKey)
    {
        StoredObjectReferenceReader.CandidateKeys(value, _r2).Should().Contain(expectedKey);
    }
}
