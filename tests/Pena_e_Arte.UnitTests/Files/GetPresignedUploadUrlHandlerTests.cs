using System.Text.RegularExpressions;
using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Files.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Files;

public class GetPresignedUploadUrlHandlerTests
{
    private readonly IR2Service _r2 = Substitute.For<IR2Service>();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public GetPresignedUploadUrlHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private GetPresignedUploadUrlHandler CreateSut() => new(_r2, _tenant);

    [Fact]
    public async Task Handle_ValidRequest_ReturnsBothUrls()
    {
        const string uploadUrl = "https://account.r2.cloudflarestorage.com/bucket/key?sig=abc";
        const string publicUrl = "https://cdn.example.com/studio-id/designs/photo.png";

        _r2.GeneratePresignedUploadUrlAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((uploadUrl, publicUrl));

        PresignUploadResponse result = await CreateSut().Handle(
            new GetPresignedUploadUrlQuery(new PresignUploadRequest("designs/photo.png", "image/png")),
            default);

        result.UploadUrl.Should().Be(uploadUrl);
        result.PublicUrl.Should().Be(publicUrl);
    }

    [Fact]
    public async Task Handle_ValidRequest_PrefixesFolderWithStudioIdAndGeneratesServerFileName()
    {
        _r2.GeneratePresignedUploadUrlAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("upload", "public"));

        await CreateSut().Handle(
            new GetPresignedUploadUrlQuery(new PresignUploadRequest("designs/photo.png", "image/png")),
            default);

        await _r2.Received(1).GeneratePresignedUploadUrlAsync(
            Arg.Is<string>(key => Regex.IsMatch(
                key, $@"^{Regex.Escape(_studioId.ToString())}/designs/[0-9a-f]{{32}}\.png$")),
            "image/png",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ObjectKeyWithLeadingSlash_StripsSlashBeforePrefixing()
    {
        _r2.GeneratePresignedUploadUrlAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("upload", "public"));

        await CreateSut().Handle(
            new GetPresignedUploadUrlQuery(new PresignUploadRequest("/designs/photo.png", "image/png")),
            default);

        await _r2.Received(1).GeneratePresignedUploadUrlAsync(
            Arg.Is<string>(key => Regex.IsMatch(
                key, $@"^{Regex.Escape(_studioId.ToString())}/designs/[0-9a-f]{{32}}\.png$")),
            "image/png",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ObjectKeyWithNoFolderPrefix_ScopesDirectlyUnderStudioId()
    {
        _r2.GeneratePresignedUploadUrlAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("upload", "public"));

        await CreateSut().Handle(
            new GetPresignedUploadUrlQuery(new PresignUploadRequest("file.bin", "image/png")),
            default);

        await _r2.Received(1).GeneratePresignedUploadUrlAsync(
            Arg.Is<string>(key => Regex.IsMatch(
                key, $@"^{Regex.Escape(_studioId.ToString())}/[0-9a-f]{{32}}\.png$")),
            "image/png",
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TwoRequestsWithIdenticalFolderPrefix_ProduceDifferentObjectKeys()
    {
        _r2.GeneratePresignedUploadUrlAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("upload", "public"));

        GetPresignedUploadUrlHandler sut = CreateSut();
        await sut.Handle(
            new GetPresignedUploadUrlQuery(new PresignUploadRequest("designs/photo.png", "image/png")),
            default);
        await sut.Handle(
            new GetPresignedUploadUrlQuery(new PresignUploadRequest("designs/photo.png", "image/png")),
            default);

        List<string> capturedKeys = _r2.ReceivedCalls()
            .Select(call => (string)call.GetArguments()[0]!)
            .ToList();

        capturedKeys.Should().HaveCount(2);
        capturedKeys[0].Should().NotBe(capturedKeys[1]);
    }

    [Theory]
    [InlineData("image/jpeg", "jpg")]
    [InlineData("image/png", "png")]
    [InlineData("image/webp", "webp")]
    [InlineData("application/pdf", "pdf")]
    public async Task Handle_AllowedContentType_GeneratedKeyExtensionMatchesContentType(
        string contentType, string expectedExtension)
    {
        _r2.GeneratePresignedUploadUrlAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("upload", "public"));

        await CreateSut().Handle(
            new GetPresignedUploadUrlQuery(new PresignUploadRequest("uploads/anything.exe", contentType)),
            default);

        await _r2.Received(1).GeneratePresignedUploadUrlAsync(
            Arg.Is<string>(key => key.EndsWith($".{expectedExtension}", StringComparison.Ordinal)),
            contentType,
            Arg.Any<CancellationToken>());
    }
}
