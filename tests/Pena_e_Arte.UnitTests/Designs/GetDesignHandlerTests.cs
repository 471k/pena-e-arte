using FluentAssertions;
using Pena_e_Arte.Application.Designs.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class GetDesignHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetDesignHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ExistingDesign_ReturnsDesignResponse()
    {
        Design design = new()
        {
            ClientId = Guid.NewGuid(),
            ArtistId = Guid.NewGuid(),
            Title = "Dragon sleeve",
            Description = "Full arm piece",
        };
        _db.Designs.Add(design);
        await _db.SaveChangesAsync();

        DesignResponse result = await CreateSut().Handle(new GetDesignQuery(design.Id), default);

        result.Id.Should().Be(design.Id);
        result.Title.Should().Be("Dragon sleeve");
        result.Status.Should().Be("Draft");
    }

    [Fact]
    public async Task Handle_DesignNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetDesignQuery(Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_DesignWithApprovedRevision_StatusIsApproved()
    {
        Design design = new() { ClientId = Guid.NewGuid(), ArtistId = Guid.NewGuid(), Title = "Rose" };
        _db.Designs.Add(design);
        DesignRevision revision = new() { DesignId = design.Id, VersionNumber = 1, FileUrl = "https://r2.example.com/v1.png" };
        _db.DesignRevisions.Add(revision);
        _db.DesignApprovals.Add(new DesignApproval { DesignRevisionId = revision.Id, Status = DesignApprovalStatus.Approved });
        await _db.SaveChangesAsync();

        DesignResponse result = await CreateSut().Handle(new GetDesignQuery(design.Id), default);

        result.Status.Should().Be("Approved");
    }

    [Fact]
    public async Task Handle_NamesTheLatestVersion_AndWhoseMoveItIs()
    {
        // E04c (A05): v1 approved, v2 uploaded since: the client owes a review of v2.
        Design design = new() { ClientId = Guid.NewGuid(), ArtistId = Guid.NewGuid(), Title = "Koi" };
        _db.Designs.Add(design);
        DateTime v2Uploaded = new(2026, 11, 5, 9, 30, 0, DateTimeKind.Utc);
        DesignRevision v1 = new() { DesignId = design.Id, VersionNumber = 1, FileUrl = "https://r2.example.com/v1.png" };
        DesignRevision v2 = new() { DesignId = design.Id, VersionNumber = 2, FileUrl = "https://r2.example.com/v2.png", UploadedAt = v2Uploaded };
        _db.DesignRevisions.AddRange(v1, v2);
        _db.DesignApprovals.Add(new DesignApproval { DesignRevisionId = v1.Id, Status = DesignApprovalStatus.Approved });
        await _db.SaveChangesAsync();

        DesignResponse result = await CreateSut().Handle(new GetDesignQuery(design.Id), default);

        result.Status.Should().Be("InReview");
        result.WaitingOn.Should().Be("Client");
        result.LatestVersionNumber.Should().Be(2);
        result.LatestUploadedAt.Should().Be(v2Uploaded);
    }

    [Fact]
    public async Task Handle_NoRevisions_WaitsOnTheArtist_WithNoLatestVersion()
    {
        Design design = new() { ClientId = Guid.NewGuid(), ArtistId = Guid.NewGuid(), Title = "Empty" };
        _db.Designs.Add(design);
        await _db.SaveChangesAsync();

        DesignResponse result = await CreateSut().Handle(new GetDesignQuery(design.Id), default);

        result.WaitingOn.Should().Be("Artist");
        result.LatestVersionNumber.Should().BeNull();
        result.LatestUploadedAt.Should().BeNull();
    }
}
