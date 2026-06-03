using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Files.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.Files;

public class GetPresignedUploadUrlHandlerTests
{
    private readonly IR2Service      _r2     = Substitute.For<IR2Service>();
    private readonly ICurrentTenant  _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid            _studioId = Guid.NewGuid();

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
    public async Task Handle_ValidRequest_PrefixesObjectKeyWithStudioId()
    {
        _r2.GeneratePresignedUploadUrlAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("upload", "public"));

        await CreateSut().Handle(
            new GetPresignedUploadUrlQuery(new PresignUploadRequest("designs/photo.png", "image/png")),
            default);

        await _r2.Received(1).GeneratePresignedUploadUrlAsync(
            $"{_studioId}/designs/photo.png",
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
            $"{_studioId}/designs/photo.png",
            "image/png",
            Arg.Any<CancellationToken>());
    }
}
