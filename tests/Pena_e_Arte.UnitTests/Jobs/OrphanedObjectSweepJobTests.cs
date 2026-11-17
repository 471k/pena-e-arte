using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Jobs;

public class OrphanedObjectSweepJobTests
{
    private const string Address = "https://media.example.com";
    private static readonly DateTimeOffset Now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Old = Now.UtcDateTime.AddDays(-30);

    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IR2Service _r2 = Substitute.For<IR2Service>();
    private readonly ListLogger<OrphanedObjectSweepJob> _logger = new();
    private readonly Guid _studio = Guid.NewGuid();

    public OrphanedObjectSweepJobTests()
    {
        R2Service real = new(Substitute.For<IAmazonS3>(), Options.Create(new R2Options { BucketName = "b", PublicUrl = Address }));
        _r2.IsR2Url(Arg.Any<string>()).Returns(ci => real.IsR2Url(ci.Arg<string>()));
        _r2.TryGetObjectKey(Arg.Any<string>()).Returns(ci => real.TryGetObjectKey(ci.Arg<string>()));
    }

    private Task Run(IAppDbContext? db = null) =>
        new OrphanedObjectSweepJob(_r2, new StoredObjectReferenceReader(db ?? _db, _r2), new FixedTimeProvider(Now), _logger)
            .RunAsync();

    private void Bucket(params R2ObjectInfo[] objects) =>
        _r2.ListByPrefixAsync("", Arg.Any<CancellationToken>()).Returns(objects);

    private string Summary => _logger.Messages(LogLevel.Information).Single(m => m.Contains("listed"));

    [Fact]
    public async Task ReportsOnlyTheUnreferencedOldObjects_AndDeletesNothing()
    {
        string referenced = $"{_studio}/portfolio/a/1.jpg";
        _db.PortfolioImages.Add(new PortfolioImage { StudioId = _studio, ArtistId = Guid.NewGuid(), ImageUrl = $"{Address}/{referenced}" });
        await _db.SaveChangesAsync();
        Bucket(
            new R2ObjectInfo(referenced, Old, 10),
            new R2ObjectInfo($"{_studio}/portfolio/a/2.jpg", Old, 20),
            new R2ObjectInfo($"{_studio}/portfolio/a/3.jpg", Old, 30),
            new R2ObjectInfo($"{_studio}/designs/d/4.png", Old, 40));

        await Run();

        _logger.Messages(LogLevel.Information).Should().Contain(
            $"OrphanedObjectSweepJob (report-only) would delete 2 object(s), 50 bytes, owner {_studio}, area portfolio");
        _logger.Messages(LogLevel.Information).Should().Contain(
            $"OrphanedObjectSweepJob (report-only) would delete 1 object(s), 40 bytes, owner {_studio}, area designs");
        Summary.Should().Contain("listed 4 object(s)").And.Contain("1 referenced").And.Contain("3 unreferenced (90 bytes)").And.Contain("deleted 0");
        await _r2.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ObjectsYoungerThanTheMinimumAge_AreNeverCandidates()
    {
        Bucket(new R2ObjectInfo($"{_studio}/appointments/pending/s/Reference/1.jpg", Now.UtcDateTime.AddDays(-6), 10));

        await Run();

        Summary.Should().Contain("1 younger than 7 days").And.Contain("0 unreferenced");
    }

    [Theory]
    [InlineData("consent/{0}/form.pdf")]
    [InlineData("reports/industry/2026-11.json")]
    public async Task ExcludedPrefixes_AreNeverCandidates(string keyFormat)
    {
        Bucket(new R2ObjectInfo(string.Format(keyFormat, _studio), Old, 10));

        await Run();

        Summary.Should().Contain("1 excluded").And.Contain("0 unreferenced");
    }

    [Fact]
    public async Task GuestPendingUploads_AreSwept_NotExcluded()
    {
        Bucket(new R2ObjectInfo($"appointments/guest-pending/{Guid.NewGuid()}/reference/1.jpg", Old, 10));

        await Run();

        _logger.Messages(LogLevel.Information).Should().Contain(
            "OrphanedObjectSweepJob (report-only) would delete 1 object(s), 10 bytes, owner platform, area appointments/guest-pending");
    }

    [Fact]
    public async Task AReferenceSourceThatFails_FailsTheRun_InsteadOfReportingEverythingAsOrphaned()
    {
        Bucket(new R2ObjectInfo($"{_studio}/portfolio/a/1.jpg", Old, 10));
        IAppDbContext broken = Substitute.For<IAppDbContext>();
        broken.PortfolioImages.Returns(_ => throw new InvalidOperationException("database unreachable"));

        Func<Task> act = () => Run(broken);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _logger.Entries.Should().BeEmpty();
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111/portfolio/a/1.jpg", "11111111-1111-1111-1111-111111111111", "portfolio")]
    [InlineData("11111111-1111-1111-1111-111111111111/portfolio/1.jpg", "11111111-1111-1111-1111-111111111111", "portfolio")]
    [InlineData("11111111-1111-1111-1111-111111111111/1.jpg", "11111111-1111-1111-1111-111111111111", "(root)")]
    [InlineData("appointments/guest-pending/g/area/1.jpg", "platform", "appointments/guest-pending")]
    [InlineData("loose-file.jpg", "platform", "(root)")]
    public void Classify_NamesTheOwnerAndArea_NeverTheFile(string key, string owner, string area)
    {
        OrphanedObjectSweepJob.Classify(key).Should().Be((owner, area));
    }
}
