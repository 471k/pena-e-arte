using System.Net;
using Amazon.S3;
using Amazon.S3.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Services;

namespace Pena_e_Arte.UnitTests.Services;

public class R2ExportServiceTests
{
    private readonly IAmazonS3 _sourceS3 = Substitute.For<IAmazonS3>();
    private readonly IAmazonS3 _backupS3 = Substitute.For<IAmazonS3>();
    private const string SourceBucket = "pena-e-arte-prod";
    private const string BackupBucket = "pena-e-arte-prod-backup";

    private R2ExportService CreateSut(string backupBucketName = BackupBucket) =>
        new(_sourceS3, _backupS3,
            Options.Create(new R2Options { BucketName = SourceBucket, BackupBucketName = backupBucketName }),
            NullLogger<R2ExportService>.Instance);

    private static AmazonS3Exception NotFound() =>
        new("not found") { StatusCode = HttpStatusCode.NotFound };

    private static GetObjectResponse FakeGetObjectResponse(string content = "data")
    {
        MemoryStream stream = new(System.Text.Encoding.UTF8.GetBytes(content));
        GetObjectResponse response = new() { ResponseStream = stream };
        response.Headers.ContentType = "image/png";
        return response;
    }

    [Fact]
    public async Task RunAsync_BackupBucketNotConfigured_SkipsWithoutCallingS3()
    {
        var result = await CreateSut(backupBucketName: "").RunAsync(default);

        result.Should().Be(new R2ExportResult(0, 0, 0));
        await _sourceS3.DidNotReceive().ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ObjectMissingInBackup_DownloadsFromSourceAndUploadsToBackup()
    {
        S3Object obj = new() { Key = "s1/portfolio/a1/img.png", ETag = "\"etag-1\"" };
        _sourceS3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { S3Objects = [obj], IsTruncated = false });
        _backupS3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns<GetObjectMetadataResponse>(_ => throw NotFound());
        _sourceS3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(FakeGetObjectResponse());

        var result = await CreateSut().RunAsync(default);

        result.Copied.Should().Be(1);
        result.Skipped.Should().Be(0);
        result.Failed.Should().Be(0);
        await _sourceS3.Received(1).GetObjectAsync(
            Arg.Is<GetObjectRequest>(r => r.BucketName == SourceBucket && r.Key == obj.Key),
            Arg.Any<CancellationToken>());
        await _backupS3.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(r => r.BucketName == BackupBucket && r.Key == obj.Key),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ObjectExistsWithMatchingETag_SkipsCopy()
    {
        S3Object obj = new() { Key = "s1/portfolio/a1/img.png", ETag = "\"same-etag\"" };
        _sourceS3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { S3Objects = [obj], IsTruncated = false });
        _backupS3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse { ETag = "\"same-etag\"" });

        var result = await CreateSut().RunAsync(default);

        result.Copied.Should().Be(0);
        result.Skipped.Should().Be(1);
        await _sourceS3.DidNotReceive().GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>());
        await _backupS3.DidNotReceive().PutObjectAsync(Arg.Any<PutObjectRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ObjectExistsWithDifferentETag_ReCopiesIt()
    {
        S3Object obj = new() { Key = "s1/portfolio/a1/img.png", ETag = "\"new-etag\"" };
        _sourceS3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { S3Objects = [obj], IsTruncated = false });
        _backupS3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns(new GetObjectMetadataResponse { ETag = "\"old-etag\"" });
        _sourceS3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(FakeGetObjectResponse());

        var result = await CreateSut().RunAsync(default);

        result.Copied.Should().Be(1);
        result.Skipped.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_MultiplePages_ProcessesAllObjects()
    {
        S3Object first = new() { Key = "a.png", ETag = "\"1\"" };
        S3Object second = new() { Key = "b.png", ETag = "\"2\"" };
        _sourceS3.ListObjectsV2Async(Arg.Is<ListObjectsV2Request>(r => r.ContinuationToken == null), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { S3Objects = [first], IsTruncated = true, NextContinuationToken = "page2" });
        _sourceS3.ListObjectsV2Async(Arg.Is<ListObjectsV2Request>(r => r.ContinuationToken == "page2"), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { S3Objects = [second], IsTruncated = false });
        _backupS3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns<GetObjectMetadataResponse>(_ => throw NotFound());
        _sourceS3.GetObjectAsync(Arg.Any<GetObjectRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ => FakeGetObjectResponse());

        var result = await CreateSut().RunAsync(default);

        result.Copied.Should().Be(2);
    }

    [Fact]
    public async Task RunAsync_OneObjectCopyFails_StillProcessesRemainingAndReportsPartialFailure()
    {
        S3Object failing = new() { Key = "failing.png", ETag = "\"1\"" };
        S3Object succeeding = new() { Key = "succeeding.png", ETag = "\"2\"" };
        _sourceS3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { S3Objects = [failing, succeeding], IsTruncated = false });
        _backupS3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns<GetObjectMetadataResponse>(_ => throw NotFound());
        _sourceS3.GetObjectAsync(Arg.Is<GetObjectRequest>(r => r.Key == failing.Key), Arg.Any<CancellationToken>())
            .Returns<GetObjectResponse>(_ => throw new AmazonS3Exception("storage unavailable"));
        _sourceS3.GetObjectAsync(Arg.Is<GetObjectRequest>(r => r.Key == succeeding.Key), Arg.Any<CancellationToken>())
            .Returns(FakeGetObjectResponse());

        var result = await CreateSut().RunAsync(default);

        result.Copied.Should().Be(1);
        result.Failed.Should().Be(1);
        await _backupS3.Received(1).PutObjectAsync(
            Arg.Is<PutObjectRequest>(r => r.Key == succeeding.Key), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_EveryObjectFails_ThrowsInsteadOfSwallowing()
    {
        S3Object obj = new() { Key = "a.png", ETag = "\"1\"" };
        _sourceS3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { S3Objects = [obj], IsTruncated = false });
        _backupS3.GetObjectMetadataAsync(Arg.Any<GetObjectMetadataRequest>(), Arg.Any<CancellationToken>())
            .Returns<GetObjectMetadataResponse>(_ => throw new AmazonS3Exception("forbidden") { StatusCode = HttpStatusCode.Forbidden });

        Func<Task> act = () => CreateSut().RunAsync(default);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RunAsync_NoObjectsInSourceBucket_ReturnsZeroResult()
    {
        _sourceS3.ListObjectsV2Async(Arg.Any<ListObjectsV2Request>(), Arg.Any<CancellationToken>())
            .Returns(new ListObjectsV2Response { S3Objects = [], IsTruncated = false });

        var result = await CreateSut().RunAsync(default);

        result.Should().Be(new R2ExportResult(0, 0, 0));
    }
}
