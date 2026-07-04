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
