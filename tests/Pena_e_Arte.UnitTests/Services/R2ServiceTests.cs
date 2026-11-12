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
}
