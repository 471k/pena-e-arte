using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Public;

public class GetPresignedGuestUploadUrlHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly IR2Service _r2 = Substitute.For<IR2Service>();

    private GetPresignedGuestUploadUrlHandler CreateSut() => new(_db, _r2);

    private static Studio MakeStudio(string slug = "guest-studio") => new()
    {
        Name = "Guest Studio", Slug = slug, City = "Porto", IsActive = true, IsPublished = true,
    };

    [Fact]
    public async Task Handle_ValidRequest_ConstructsServerSideKeyUnderGuestPendingPrefix()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        _r2.GeneratePresignedUploadUrlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("upload-url", "public-url"));

        PresignUploadResponse result = await CreateSut().Handle(
            new GetPresignedGuestUploadUrlQuery(studio.Slug, new PresignGuestUploadRequest("image/png", "area")),
            default);

        result.UploadUrl.Should().Be("upload-url");
        result.PublicUrl.Should().Be("public-url");
        await _r2.Received(1).GeneratePresignedUploadUrlAsync(
            Arg.Is<string>(key => key.StartsWith($"appointments/guest-pending/{studio.Id}/area/") && key.EndsWith(".png")),
            "image/png", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ReferenceCategory_UsesReferenceInKey()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        _r2.GeneratePresignedUploadUrlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("u", "p"));

        await CreateSut().Handle(
            new GetPresignedGuestUploadUrlQuery(studio.Slug, new PresignGuestUploadRequest("image/jpeg", "reference")),
            default);

        await _r2.Received(1).GeneratePresignedUploadUrlAsync(
            Arg.Is<string>(key => key.Contains($"/{studio.Id}/reference/") && key.EndsWith(".jpg")),
            "image/jpeg", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_UnknownSlug_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new GetPresignedGuestUploadUrlQuery("no-such-slug", new PresignGuestUploadRequest("image/png", "area")),
            default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_TwoRequests_ProduceDifferentObjectKeys()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        _r2.GeneratePresignedUploadUrlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(("u", "p"));

        GetPresignedGuestUploadUrlHandler sut = CreateSut();
        await sut.Handle(new GetPresignedGuestUploadUrlQuery(studio.Slug, new PresignGuestUploadRequest("image/png", "area")), default);
        await sut.Handle(new GetPresignedGuestUploadUrlQuery(studio.Slug, new PresignGuestUploadRequest("image/png", "area")), default);

        List<string> keys = _r2.ReceivedCalls().Select(c => (string)c.GetArguments()[0]!).ToList();
        keys.Should().HaveCount(2);
        keys[0].Should().NotBe(keys[1]);
    }
}
