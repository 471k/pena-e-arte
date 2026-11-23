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

    private Task Run(IAppDbContext? db = null, string? mode = null) =>
        new OrphanedObjectSweepJob(_r2, new StoredObjectReferenceReader(db ?? _db, _r2), new FixedTimeProvider(Now),
                Options.Create(new OrphanedObjectSweepOptions { Mode = mode }), _logger)
            .RunAsync();

    private void Bucket(params R2ObjectInfo[] objects) =>
        _r2.ListAllExceptAsync(Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>()).Returns(objects);

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
        await _r2.DidNotReceive().CopyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ObjectsYoungerThanTheMinimumAge_AreNeverCandidates()
    {
        Bucket(new R2ObjectInfo($"{_studio}/appointments/pending/s/Reference/1.jpg", Now.UtcDateTime.AddDays(-6), 10));

        await Run();

        Summary.Should().Contain("1 younger than 7 days").And.Contain("0 unreferenced");
    }

    // The boundary: exactly at the cutoff is still too young (>=), one second past it is a candidate.
    [Fact]
    public async Task AnObjectExactlyAtTheCutoff_IsStillTooYoung_OneSecondOlderIsACandidate()
    {
        DateTime cutoff = Now.UtcDateTime - OrphanedObjectSweepJob.MinimumAge;
        Bucket(
            new R2ObjectInfo($"{_studio}/portfolio/a/at.jpg", cutoff, 10),
            new R2ObjectInfo($"{_studio}/portfolio/a/past.jpg", cutoff.AddSeconds(-1), 20));

        await Run();

        Summary.Should().Contain("1 younger than 7 days").And.Contain("1 unreferenced (20 bytes)");
    }

    // The G-1 shape: the same instant arriving with a non-UTC Kind must give the same verdict. Runs the real
    // R2Service listing (S3 faked). Both sides of the cutoff, because a zone ahead of UTC shifts a Local value
    // one way and a wrong ToUniversalTime() on an Unspecified value shifts it the other: on a non-UTC machine
    // (Paris here) one of the two cases flips if the normalization regresses. On a UTC runner they agree.
    [Theory]
    [InlineData(30, 3, 0)]   // 30 min younger than the cutoff: all three too young
    [InlineData(-30, 0, 3)]  // 30 min older: all three candidates
    public async Task TheSameInstantInAnyKind_GetsTheSameVerdict(int minutesFromCutoff, int young, int unreferenced)
    {
        DateTime justYoung = Now.UtcDateTime - OrphanedObjectSweepJob.MinimumAge + TimeSpan.FromMinutes(minutesFromCutoff);
        IAmazonS3 s3 = Substitute.For<IAmazonS3>();
        s3.ListObjectsV2Async(Arg.Any<Amazon.S3.Model.ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new Amazon.S3.Model.ListObjectsV2Response
            {
                S3Objects =
                [
                    new Amazon.S3.Model.S3Object { Key = $"{_studio}/portfolio/a/utc.jpg", LastModified = justYoung, Size = 1 },
                    new Amazon.S3.Model.S3Object { Key = $"{_studio}/portfolio/a/local.jpg", LastModified = justYoung.ToLocalTime(), Size = 1 },
                    new Amazon.S3.Model.S3Object { Key = $"{_studio}/portfolio/a/unspec.jpg", LastModified = DateTime.SpecifyKind(justYoung, DateTimeKind.Unspecified), Size = 1 },
                ],
            });
        R2Service r2 = new(s3, Options.Create(new R2Options { BucketName = "b", PublicUrl = Address }));

        await new OrphanedObjectSweepJob(r2, new StoredObjectReferenceReader(_db, r2), new FixedTimeProvider(Now),
            Options.Create(new OrphanedObjectSweepOptions()), _logger).RunAsync();

        Summary.Should().Contain($"{young} younger than 7 days").And.Contain($"{unreferenced} unreferenced");
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

    // ---- Phase 3: the mode switch and the quarantine move ----

    private const string QuarantineDay = "_quarantine/2026-11-17/";

    private void CopiesVerify() =>
        _r2.GetObjectSizeAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => SizeOf(ci.Arg<string>()[QuarantineDay.Length..]));

    private readonly Dictionary<string, long> _sizes = [];
    private long? SizeOf(string key) => _sizes.TryGetValue(key, out long size) ? size : null;

    private R2ObjectInfo Obj(string key, long size, DateTime? at = null)
    {
        _sizes[key] = size;
        return new R2ObjectInfo(key, at ?? Old, size);
    }

    private string QuarantineSummary =>
        _logger.Entries.Single(e => e.Message.StartsWith("OrphanedObjectSweepJob (quarantine) listed")).Message;


    // A row pointing at an object outside the bucket: the reader returns at least one key, so the
    // "no keys at all" brake doesn't fire in tests that are about something else.
    private async Task AnyReference()
    {
        _db.PortfolioImages.Add(new PortfolioImage { StudioId = _studio, ArtistId = Guid.NewGuid(), ImageUrl = $"{Address}/{_studio}/portfolio/a/kept-elsewhere.jpg" });
        await _db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ReportOnly")]
    public async Task WithoutTheSwitch_TheJobIsReportOnly_AndTouchesNothing(string? mode)
    {
        Bucket(Obj($"{_studio}/portfolio/a/1.jpg", 10));

        await Run(mode: mode);

        Summary.Should().StartWith("OrphanedObjectSweepJob (report-only)");
        await _r2.DidNotReceive().CopyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _r2.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // A typo must never switch the move on, and must not pass unnoticed either.
    [Theory]
    [InlineData("Quarantined")]
    [InlineData("Delete")]
    [InlineData("HardDelete")]
    public async Task AnUnknownMode_RunsReportOnly_AndSaysSo(string mode)
    {
        Bucket(Obj($"{_studio}/portfolio/a/1.jpg", 10));

        await Run(mode: mode);

        _logger.Messages(LogLevel.Warning).Should().ContainSingle(m => m.Contains($"'{mode}'") && m.Contains("running report-only"));
        Summary.Should().StartWith("OrphanedObjectSweepJob (report-only)");
        await _r2.DidNotReceive().CopyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _r2.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task EveryRun_SkipsTheQuarantineFolderAtTheListCall()
    {
        Bucket();

        await Run();
        await Run(mode: "Quarantine");

        await _r2.Received(2).ListAllExceptAsync(
            Arg.Is<IReadOnlyCollection<string>>(c => c.Contains("_quarantine/")), Arg.Any<CancellationToken>());
        await _r2.DidNotReceive().ListByPrefixAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Quarantine_MovesOnlyTheUnreferencedOldObjects_CopyThenVerifyThenDelete()
    {
        string referenced = $"{_studio}/portfolio/a/1.jpg";
        string orphan = $"{_studio}/portfolio/a/2.jpg";
        _db.PortfolioImages.Add(new PortfolioImage { StudioId = _studio, ArtistId = Guid.NewGuid(), ImageUrl = $"{Address}/{referenced}" });
        await _db.SaveChangesAsync();
        Bucket(
            Obj(referenced, 10),
            Obj(orphan, 20),
            Obj($"{_studio}/portfolio/a/young.jpg", 30, Now.UtcDateTime.AddDays(-1)),
            Obj($"consent/{_studio}/form.pdf", 40));
        CopiesVerify();

        await Run(mode: "Quarantine");

        Received.InOrder(() =>
        {
            _r2.CopyAsync(orphan, QuarantineDay + orphan, Arg.Any<CancellationToken>());
            _r2.GetObjectSizeAsync(QuarantineDay + orphan, Arg.Any<CancellationToken>());
            _r2.DeleteAsync(orphan, Arg.Any<CancellationToken>());
        });
        await _r2.Received(1).CopyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _r2.Received(1).DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        _logger.Messages(LogLevel.Information).Should().Contain(
            $"OrphanedObjectSweepJob (quarantine) moved 1 object(s), 20 bytes, owner {_studio}, area portfolio to _quarantine/2026-11-17/");
        QuarantineSummary.Should()
            .Contain("listed 4 object(s): 1 excluded, 1 younger than 7 days, 1 referenced, 1 unreferenced (20 bytes)")
            .And.Contain("moved 1 (20 bytes) to _quarantine/2026-11-17/")
            .And.Contain("0 copy failed, 0 copy not verified, 0 copied but original kept; hard-deleted 0")
            .And.Contain("_quarantine/ not listed");
    }

    [Fact]
    public async Task AFailedCopy_LeavesTheOriginal_AndTheNextObjectStillMoves()
    {
        await AnyReference();
        string bad = $"{_studio}/portfolio/a/bad.jpg";
        string good = $"{_studio}/portfolio/a/good.jpg";
        Bucket(Obj(bad, 10), Obj(good, 20));
        CopiesVerify();
        _r2.CopyAsync(bad, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new AmazonS3Exception("boom") { ErrorCode = "InternalError", StatusCode = System.Net.HttpStatusCode.InternalServerError });

        await Run(mode: "Quarantine");

        await _r2.DidNotReceive().DeleteAsync(bad, Arg.Any<CancellationToken>());
        await _r2.Received(1).DeleteAsync(good, Arg.Any<CancellationToken>());
        _logger.Messages(LogLevel.Warning).Should().ContainSingle(m =>
            m.Contains("copy failed for one object") && m.Contains("InternalError (500)") && m.Contains("the original stays in place"));
        QuarantineSummary.Should().Contain("moved 1 (20 bytes)").And.Contain("1 copy failed");
    }

    // The copy must read back at the original's size before the original is deleted.
    [Theory]
    [InlineData(19L)]
    [InlineData(null)]
    public async Task ACopyThatDoesNotVerify_NeverDeletesTheOriginal(long? copiedSize)
    {
        await AnyReference();
        string orphan = $"{_studio}/portfolio/a/2.jpg";
        Bucket(Obj(orphan, 20), Obj($"{_studio}/portfolio/a/3.jpg", 30));
        CopiesVerify();
        _r2.GetObjectSizeAsync(QuarantineDay + orphan, Arg.Any<CancellationToken>()).Returns(copiedSize);

        await Run(mode: "Quarantine");

        await _r2.DidNotReceive().DeleteAsync(orphan, Arg.Any<CancellationToken>());
        QuarantineSummary.Should().Contain("1 copy not verified").And.Contain("moved 1 (30 bytes)");
    }

    [Fact]
    public async Task AFailedDelete_IsCountedAsCopiedButKept()
    {
        await AnyReference();
        string orphan = $"{_studio}/portfolio/a/2.jpg";
        Bucket(Obj(orphan, 20), Obj($"{_studio}/portfolio/a/3.jpg", 30));
        CopiesVerify();
        _r2.DeleteAsync(orphan, Arg.Any<CancellationToken>())
            .Returns(_ => throw new AmazonS3Exception("locked") { ErrorCode = "ObjectLockedByBucketPolicy", StatusCode = System.Net.HttpStatusCode.Conflict });

        await Run(mode: "Quarantine");

        _logger.Messages(LogLevel.Warning).Should().ContainSingle(m =>
            m.Contains("delete failed") && m.Contains("ObjectLockedByBucketPolicy (409)") && m.Contains("a copy is in quarantine"));
        QuarantineSummary.Should().Contain("1 copied but original kept").And.Contain("moved 1 (30 bytes)");
    }

    // Every move failing is systemic (lost permission, a lock on the wrong prefix): fail the job so it is seen.
    [Fact]
    public async Task EveryMoveFailing_FailsTheJob_AfterLoggingTheSummary()
    {
        await AnyReference();
        Bucket(Obj($"{_studio}/portfolio/a/2.jpg", 20));
        _r2.CopyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new AmazonS3Exception("denied") { ErrorCode = "AccessDenied", StatusCode = System.Net.HttpStatusCode.Forbidden });

        Func<Task> act = () => Run(mode: "Quarantine");

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*all 1 move(s) failed*");
        QuarantineSummary.Should().Contain("moved 0 (0 bytes)").And.Contain("1 copy failed");
    }

    [Fact]
    public async Task AReferenceReaderThatReturnsNoKeys_RefusesToMoveAnything()
    {
        Bucket(Obj($"{_studio}/portfolio/a/1.jpg", 10));

        await Run(mode: "Quarantine");

        _logger.Messages(LogLevel.Error).Should().ContainSingle(m =>
            m.StartsWith("OrphanedObjectSweepJob (quarantine, refused)") && m.Contains("moved 0 because the reference reader returned no keys at all"));
        await _r2.DidNotReceive().CopyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _r2.DidNotReceive().DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    // The brake's boundary: at 20 candidates, 10 unreferenced (exactly half) moves; 11 refuses.
    [Theory]
    [InlineData(10, false)]
    [InlineData(11, true)]
    public async Task MoreThanHalfOfTheCandidatesUnreferenced_RefusesToMove(int unreferenced, bool refused)
    {
        List<R2ObjectInfo> objects = [];
        for (int i = 0; i < 20; i++)
        {
            string key = $"{_studio}/portfolio/a/{i}.jpg";
            objects.Add(Obj(key, 1));
            if (i >= unreferenced)
                _db.PortfolioImages.Add(new PortfolioImage { StudioId = _studio, ArtistId = Guid.NewGuid(), ImageUrl = $"{Address}/{key}" });
        }
        await _db.SaveChangesAsync();
        Bucket([.. objects]);
        CopiesVerify();

        await Run(mode: "Quarantine");

        if (refused)
        {
            _logger.Messages(LogLevel.Error).Should().ContainSingle(m => m.Contains("moved 0 because 11 of 20 candidates look unreferenced, more than 50%"));
            await _r2.DidNotReceive().CopyAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
        else
        {
            _logger.Messages(LogLevel.Error).Should().BeEmpty();
            await _r2.Received(10).DeleteAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        }
    }

    [Theory]
    [InlineData(0, 0, 5, null)]
    [InlineData(2, 4, 5, null)]          // production's shape: a small bucket never trips the share rule
    [InlineData(1, 0, 0, "no keys")]
    [InlineData(19, 0, 5, null)]         // below the minimum count
    [InlineData(11, 9, 5, "11 of 20")]
    public void RefusalReason_Boundaries(int unreferenced, int referenced, int keys, string? expected)
    {
        string? reason = OrphanedObjectSweepJob.RefusalReason(unreferenced, referenced, keys);

        if (expected is null) reason.Should().BeNull();
        else reason.Should().Contain(expected);
    }

    // This job logs counts, owners and areas, never a key, in either mode or on any failure path.
    [Fact]
    public async Task NoLogLine_EverContainsAKey()
    {
        await AnyReference();
        Bucket(Obj($"{_studio}/portfolio/a/secretname.jpg", 10), Obj($"{_studio}/designs/d/othername.png", 20));
        _r2.CopyAsync($"{_studio}/designs/d/othername.png", Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => throw new AmazonS3Exception($"could not copy {_studio}/designs/d/othername.png") { ErrorCode = "InternalError" });
        CopiesVerify();

        await Run();
        await Run(mode: "Quarantine");

        _logger.Entries.Should().NotBeEmpty().And.OnlyContain(e => !e.Message.Contains("secretname") && !e.Message.Contains("othername"));
    }
}
