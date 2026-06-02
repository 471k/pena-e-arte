using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class UploadDesignRevisionHandlerTests
{
    private readonly FakeDbContext     _db       = FakeDbContext.Create();
    private readonly ICurrentTenant    _tenant   = Substitute.For<ICurrentTenant>();
    private readonly IRealtimeNotifier _realtime = Substitute.For<IRealtimeNotifier>();
    private readonly Guid              _studioId = Guid.NewGuid();

    public UploadDesignRevisionHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private UploadDesignRevisionHandler CreateSut() => new(_db, _tenant, _realtime);

    [Fact]
    public async Task Handle_FirstRevision_SetsVersionNumberToOne()
    {
        Guid designId = await SeedDesign();

        DesignRevisionResponse result = await CreateSut()
            .Handle(new UploadDesignRevisionCommand(new(designId, "https://r2.example.com/v1.png", null)), default);

        result.VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task Handle_SecondRevision_IncrementsVersionNumber()
    {
        Guid designId = await SeedDesign();
        await SeedRevision(designId, version: 1);

        DesignRevisionResponse result = await CreateSut()
            .Handle(new UploadDesignRevisionCommand(new(designId, "https://r2.example.com/v2.png", null)), default);

        result.VersionNumber.Should().Be(2);
    }

    [Fact]
    public async Task Handle_ValidRevision_PersistsToDb()
    {
        Guid designId = await SeedDesign();

        await CreateSut()
            .Handle(new UploadDesignRevisionCommand(new(designId, "https://r2.example.com/v1.png", "First draft")), default);

        _db.DesignRevisions.Should().ContainSingle(r =>
            r.DesignId == designId &&
            r.Notes == "First draft");
    }

    [Fact]
    public async Task Handle_DesignNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut()
            .Handle(new UploadDesignRevisionCommand(new(Guid.NewGuid(), "https://r2.example.com/v1.png", null)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ValidRevision_NotifiesRealtimeWithDesignUploaded()
    {
        Guid designId = await SeedDesign();

        await CreateSut()
            .Handle(new UploadDesignRevisionCommand(new(designId, "https://r2.example.com/v1.png", null)), default);

        await _realtime.Received(1)
            .NotifyStudioAsync(_studioId, "DesignUploaded", Arg.Any<object>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidRevision_ReturnsCorrectFileUrl()
    {
        Guid designId = await SeedDesign();
        const string url = "https://r2.example.com/design-v1.png";

        DesignRevisionResponse result = await CreateSut()
            .Handle(new UploadDesignRevisionCommand(new(designId, url, null)), default);

        result.FileUrl.Should().Be(url);
    }

    private async Task<Guid> SeedDesign()
    {
        Design design = new() { StudioId = _studioId, ClientId = Guid.NewGuid(), ArtistId = Guid.NewGuid(), Title = "Rose" };
        _db.Designs.Add(design);
        await _db.SaveChangesAsync();
        return design.Id;
    }

    private async Task SeedRevision(Guid designId, int version)
    {
        _db.DesignRevisions.Add(new DesignRevision
        {
            StudioId      = _studioId,
            DesignId      = designId,
            VersionNumber = version,
            FileUrl       = $"https://r2.example.com/v{version}.png",
            UploadedAt    = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();
    }
}
