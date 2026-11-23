using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.UnitTests.Services;

public class R2ServiceTests
{
    private readonly IAmazonS3 _s3 = Substitute.For<IAmazonS3>();

    private R2Service CreateSut() =>
        new(_s3, Options.Create(new R2Options { BucketName = "test-bucket" }));

    private const string NewAddress = "https://media.example.com";
    private const string OldAddress = "https://bucket.old-worker.example.dev/";

    private R2Service CreateMovedSut(params string[] legacy) =>
        new(_s3, Options.Create(new R2Options
        {
            BucketName = "test-bucket",
            PublicUrl = NewAddress,
            LegacyPublicUrls = legacy,
        }));

    [Theory]
    [InlineData("https://media.example.com/s1/portfolio/a.jpg", true)]
    [InlineData("https://bucket.old-worker.example.dev/s1/portfolio/a.jpg", true)]
    [InlineData("https://MEDIA.example.com/s1/portfolio/a.jpg", true)]
    [InlineData("https://media.example.com.evil.test/s1/portfolio/a.jpg", false)]
    [InlineData("https://elsewhere.test/s1/portfolio/a.jpg", false)]
    [InlineData("", false)]
    public void IsR2Url_AcceptsTheCurrentAndOldAddresses_AndNothingThatOnlyStartsLikeThem(string url, bool expected)
    {
        CreateMovedSut(OldAddress).IsR2Url(url).Should().Be(expected);
    }

    [Fact]
    public void IsR2Url_WithNoOldAddressConfigured_RejectsTheOldOne()
    {
        CreateMovedSut().IsR2Url("https://bucket.old-worker.example.dev/s1/a.jpg").Should().BeFalse();
    }

    [Fact]
    public void GetKnownPublicUrls_ListsTheCurrentAddressFirst_AndIgnoresBlankEntries()
    {
        // An unset GitHub secret arrives as an empty string.
        IReadOnlyList<string> urls = CreateMovedSut("", OldAddress, "  ").GetKnownPublicUrls("s1/a.jpg");

        urls.Should().Equal(
            "https://media.example.com/s1/a.jpg",
            "https://bucket.old-worker.example.dev/s1/a.jpg");
    }

    [Fact]
    public void GetPublicUrl_GivesNewUploadsTheCurrentAddressOnly()
    {
        CreateMovedSut(OldAddress).GetPublicUrl("s1/a.jpg").Should().Be("https://media.example.com/s1/a.jpg");
    }

    [Theory]
    [InlineData("https://media.example.com/consent/s1/f.pdf")]
    [InlineData("https://bucket.old-worker.example.dev/consent/s1/f.pdf")]
    public async Task GeneratePresignedReadUrlAsync_SignsTheSameObjectKey_WhicheverAddressTheRowHolds(string storedUrl)
    {
        _s3.GetPreSignedURL(Arg.Any<GetPreSignedUrlRequest>()).Returns("https://signed.example/x");

        await CreateMovedSut(OldAddress).GeneratePresignedReadUrlAsync(storedUrl, default);
        await CreateMovedSut(OldAddress).GeneratePresignedReadUrlAsync(storedUrl, TimeSpan.FromMinutes(5), default);

        _s3.Received(2).GetPreSignedURL(Arg.Is<GetPreSignedUrlRequest>(r =>
            r.BucketName == "test-bucket" && r.Key == "consent/s1/f.pdf" && r.Verb == HttpVerb.GET));
    }

    [Fact]
    public async Task UploadAsync_SendsUnsignedPayload_BecauseR2RejectsStreamingSignatures()
    {
        await CreateSut().UploadAsync("reports/industry/2026-10.json", [1, 2, 3], "application/json", default);

        await _s3.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(r =>
                r.BucketName == "test-bucket" &&
                r.Key == "reports/industry/2026-10.json" &&
                r.ContentType == "application/json" &&
                r.DisablePayloadSigning == true),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListByPrefixAsync_WhenNoObjectsMatchPrefix_ReturnsEmptyList()
    {
        // S3-compatible providers return S3Objects == null (not an empty list)
        // when a prefix has zero matches.
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { S3Objects = null! });

        IReadOnlyList<R2ObjectInfo> result = await CreateSut().ListByPrefixAsync("reports/industry/", default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListByPrefixAsync_MapsMatchingObjects()
    {
        DateTime lastModified = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response
            {
                S3Objects = [new S3Object { Key = "reports/industry/2026-06.json", LastModified = lastModified, Size = 1024L }]
            });

        IReadOnlyList<R2ObjectInfo> result = await CreateSut().ListByPrefixAsync("reports/industry/", default);

        result.Should().ContainSingle();
        result[0].Key.Should().Be("reports/industry/2026-06.json");
        result[0].LastModified.Should().Be(lastModified);
        result[0].SizeBytes.Should().Be(1024L);
    }

    // The sweep verifies a quarantine copy against the source's ETag, which comes from the listing.
    [Fact]
    public async Task BothListings_CarryTheETag()
    {
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { S3Objects = [new S3Object { Key = "k.jpg", Size = 1L, ETag = "\"abc\"" }] });

        (await CreateSut().ListByPrefixAsync("", default)).Single().ETag.Should().Be("\"abc\"");
        (await CreateSut().ListAllExceptAsync(["_quarantine/"], default)).Single().ETag.Should().Be("\"abc\"");
    }

    // ListObjectsV2 returns at most 1,000 keys per call; only the first page used to be read.
    [Fact]
    public async Task ListByPrefixAsync_FollowsTheContinuationTokenAcrossPages()
    {
        _s3.ListObjectsV2Async(Arg.Is<ListObjectsV2Request>(r => r.ContinuationToken == null), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response
            {
                S3Objects = [new S3Object { Key = "s1/a.jpg", Size = 1L }],
                IsTruncated = true,
                NextContinuationToken = "page-2",
            });
        _s3.ListObjectsV2Async(Arg.Is<ListObjectsV2Request>(r => r.ContinuationToken == "page-2"), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response
            {
                S3Objects = [new S3Object { Key = "s1/b.jpg", Size = 2L }],
                IsTruncated = false,
            });

        IReadOnlyList<R2ObjectInfo> result = await CreateSut().ListByPrefixAsync("s1/", default);

        result.Select(o => o.Key).Should().Equal("s1/a.jpg", "s1/b.jpg");
    }

    // LastModified leaves the listing as Kind.Utc whatever Kind the SDK hands back, so the sweep's age
    // comparison never depends on the pod's time zone (Desktop, 18 Nov).
    [Fact]
    public async Task ListByPrefixAsync_ReturnsUtc_ForLocalUnspecifiedAndUtcTimestamps_AtTheSameInstant()
    {
        DateTime instant = new(2026, 11, 1, 10, 30, 0, DateTimeKind.Utc);
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response
            {
                S3Objects =
                [
                    new S3Object { Key = "utc", LastModified = instant },
                    new S3Object { Key = "local", LastModified = instant.ToLocalTime() },
                    new S3Object { Key = "unspecified", LastModified = DateTime.SpecifyKind(instant, DateTimeKind.Unspecified) },
                ],
            });

        IReadOnlyList<R2ObjectInfo> result = await CreateSut().ListByPrefixAsync("", default);

        result.Should().OnlyContain(o => o.LastModified.Kind == DateTimeKind.Utc && o.LastModified == instant);
    }

    [Fact]
    public async Task ListByPrefixAsync_MissingTimestamp_ReadsAsNow_SoNeverOldEnoughToSweep()
    {
        _s3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { S3Objects = [new S3Object { Key = "k", LastModified = null }] });

        IReadOnlyList<R2ObjectInfo> result = await CreateSut().ListByPrefixAsync("", default);

        result[0].LastModified.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromMinutes(1));
    }

    [Theory]
    [InlineData("https://media.example.com/s1/portfolio/a.jpg", "s1/portfolio/a.jpg")]
    [InlineData("https://bucket.old-worker.example.dev/s1/a.jpg", "s1/a.jpg")]
    [InlineData("https://MEDIA.example.com/s1/a.jpg", "s1/a.jpg")]
    [InlineData("https://media.example.com.evil.test/s1/a.jpg", null)]
    [InlineData("https://elsewhere.test/s1/a.jpg", null)]
    [InlineData("https://media.example.com/", null)]
    [InlineData("", null)]
    public void TryGetObjectKey_OnlyForAKnownAddress(string url, string? expected)
    {
        CreateMovedSut(OldAddress).TryGetObjectKey(url).Should().Be(expected);
    }

    // The quarantine folder is excluded AT the list call: its objects are never requested at all.
    [Fact]
    public async Task ListAllExceptAsync_NeverRequestsASkippedFolder_AndListsEveryOtherOne()
    {
        _s3.ListObjectsV2Async(Arg.Is<ListObjectsV2Request>(r => r.Delimiter == "/" && r.ContinuationToken == null), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response
            {
                S3Objects = [new S3Object { Key = "loose.jpg", Size = 1L }],
                CommonPrefixes = ["s1/"],
                IsTruncated = true,
                NextContinuationToken = "top-2",
            });
        _s3.ListObjectsV2Async(Arg.Is<ListObjectsV2Request>(r => r.Delimiter == "/" && r.ContinuationToken == "top-2"), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { CommonPrefixes = ["_quarantine/", "s2/"] });
        _s3.ListObjectsV2Async(Arg.Is<ListObjectsV2Request>(r => r.Delimiter == null && r.Prefix == "s1/"), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { S3Objects = [new S3Object { Key = "s1/a/1.jpg", Size = 2L }] });
        _s3.ListObjectsV2Async(Arg.Is<ListObjectsV2Request>(r => r.Delimiter == null && r.Prefix == "s2/"), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { S3Objects = [new S3Object { Key = "s2/b/2.jpg", Size = 3L }] });

        IReadOnlyList<R2ObjectInfo> result = await CreateSut().ListAllExceptAsync(["_quarantine/"], default);

        result.Select(o => o.Key).Should().BeEquivalentTo("loose.jpg", "s1/a/1.jpg", "s2/b/2.jpg");
        await _s3.DidNotReceive().ListObjectsV2Async(
            Arg.Is<ListObjectsV2Request>(r => r.Prefix != null && r.Prefix.StartsWith("_quarantine/")), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("_quarantine")]
    [InlineData("s1/_quarantine/")]
    [InlineData("/")]
    public async Task ListAllExceptAsync_RejectsAnythingButATopLevelFolder(string folder)
    {
        Func<Task> act = () => CreateSut().ListAllExceptAsync([folder], default);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CopyAsync_CopiesInsideTheOneBucket()
    {
        await CreateSut().CopyAsync("s1/a.jpg", "_quarantine/2026-11-23/s1/a.jpg", default);

        await _s3.Received(1).CopyObjectAsync(
            Arg.Is<CopyObjectRequest>(r =>
                r.SourceBucket == "test-bucket" && r.SourceKey == "s1/a.jpg" &&
                r.DestinationBucket == "test-bucket" && r.DestinationKey == "_quarantine/2026-11-23/s1/a.jpg"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HeadAsync_ReturnsSizeAndETag_OrNullWhenTheKeyIsAbsent()
    {
        _s3.GetObjectMetadataAsync(Arg.Is<GetObjectMetadataRequest>(r => r.Key == "there"), Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse { ContentLength = 75L, ETag = "\"abc\"" });
        _s3.GetObjectMetadataAsync(Arg.Is<GetObjectMetadataRequest>(r => r.Key == "gone"), Arg.Any<CancellationToken>())
            .Returns<GetObjectMetadataResponse>(_ => throw new AmazonS3Exception("Not Found") { StatusCode = System.Net.HttpStatusCode.NotFound });

        (await CreateSut().HeadAsync("there", default)).Should().Be(new R2ObjectStat(75L, "\"abc\""));
        (await CreateSut().HeadAsync("gone", default)).Should().BeNull();
    }

    // Anything other than "not found" is not an answer about the object, so it must not read as "absent".
    [Fact]
    public async Task HeadAsync_OtherErrors_Throw()
    {
        _s3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns<GetObjectMetadataResponse>(_ => throw new AmazonS3Exception("Forbidden") { StatusCode = System.Net.HttpStatusCode.Forbidden });

        Func<Task> act = () => CreateSut().HeadAsync("k", default);

        await act.Should().ThrowAsync<AmazonS3Exception>();
    }
}
