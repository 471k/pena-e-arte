using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class MarkDesignAsCatalogItemHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly Guid _studioId = Guid.NewGuid();

    private MarkDesignAsCatalogItemHandler CreateSut(ICurrentUser? user = null) =>
        new(_db, user ?? FakeCurrentUser.Owner());

    private Design SeedDesign(Guid? clientId = null)
    {
        Design design = new()
        {
            StudioId = _studioId,
            ClientId = clientId ?? Guid.NewGuid(),
            ArtistId = Guid.NewGuid(),
            Title = "Rose tattoo",
        };
        _db.Designs.Add(design);
        _db.SaveChanges();
        return design;
    }

    [Fact]
    public async Task Handle_MarkAsCatalog_ClearsClientIdAndSetsPrice()
    {
        Design design = SeedDesign();

        DesignResponse result = await CreateSut().Handle(
            new MarkDesignAsCatalogItemCommand(design.Id, new MarkDesignAsCatalogItemRequest(true, 150m)), default);

        result.IsCatalogItem.Should().BeTrue();
        result.Price.Should().Be(150m);
        result.ClientId.Should().BeNull();
        _db.Designs.Single(d => d.Id == design.Id).ClientId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_Unmark_ClearsPrice()
    {
        Design design = SeedDesign();
        await CreateSut().Handle(
            new MarkDesignAsCatalogItemCommand(design.Id, new MarkDesignAsCatalogItemRequest(true, 150m)), default);

        DesignResponse result = await CreateSut().Handle(
            new MarkDesignAsCatalogItemCommand(design.Id, new MarkDesignAsCatalogItemRequest(false, null)), default);

        result.IsCatalogItem.Should().BeFalse();
        result.Price.Should().BeNull();
    }

    [Fact]
    public async Task Handle_DesignWithApprovedRevisionAndClient_ThrowsBusinessRuleViolation()
    {
        Design design = SeedDesign();
        DesignRevision revision = new() { StudioId = _studioId, DesignId = design.Id, VersionNumber = 1, FileUrl = "https://r2/x.png" };
        _db.DesignRevisions.Add(revision);
        _db.SaveChanges();
        _db.DesignApprovals.Add(new DesignApproval
        {
            StudioId = _studioId, DesignRevisionId = revision.Id, Status = DesignApprovalStatus.Approved,
        });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        Func<Task> act = () => CreateSut().Handle(
            new MarkDesignAsCatalogItemCommand(design.Id, new MarkDesignAsCatalogItemRequest(true, 100m)), default);

        await act.Should().ThrowAsync<BusinessRuleViolationException>();
    }

    [Fact]
    public async Task Handle_UnknownDesign_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(
            new MarkDesignAsCatalogItemCommand(Guid.NewGuid(), new MarkDesignAsCatalogItemRequest(true, 100m)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ArtistNotOwningDesign_ThrowsNotFoundException()
    {
        Design design = SeedDesign();
        FakeCurrentUser otherArtist = FakeCurrentUser.Artist();
        _db.Artists.Add(new Artist { StudioId = _studioId, UserId = otherArtist.UserId, FirstName = "A", LastName = "B", Email = "a@b.com" });
        _db.SaveChanges();

        Func<Task> act = () => CreateSut(otherArtist).Handle(
            new MarkDesignAsCatalogItemCommand(design.Id, new MarkDesignAsCatalogItemRequest(true, 100m)), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
