using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class UpdateMyStudioHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant = Substitute.For<ICurrentTenant>();
    private readonly Guid _studioId = Guid.NewGuid();

    public UpdateMyStudioHandlerTests() => _tenant.StudioId.Returns(_studioId);

    private UpdateMyStudioHandler CreateSut() => new(_db, _tenant);

    private async Task SeedStudio()
    {
        Studio studio = new() { Id = _studioId, Name = "Old Name", Slug = "old-slug", City = "Lisbon" };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsPhoneAndInstagram()
    {
        await SeedStudio();
        UpdateStudioRequest req = new("New Name", "Porto", 41.1, -8.6, "+351 912 345 678", "@my_studio");

        StudioResponse result = await CreateSut().Handle(new UpdateMyStudioCommand(req), default);

        result.PhoneNumber.Should().Be("+351 912 345 678");
        result.InstagramHandle.Should().Be("my_studio");
        _db.Studios.Single(s => s.Id == _studioId).PhoneNumber.Should().Be("+351 912 345 678");
        _db.Studios.Single(s => s.Id == _studioId).InstagramHandle.Should().Be("my_studio");
    }

    [Fact]
    public async Task Handle_InstagramHandleWithLeadingAt_StripsAt()
    {
        await SeedStudio();
        UpdateStudioRequest req = new("New Name", "Porto", 41.1, -8.6, null, "@handle");

        StudioResponse result = await CreateSut().Handle(new UpdateMyStudioCommand(req), default);

        result.InstagramHandle.Should().Be("handle");
    }

    [Fact]
    public async Task Handle_EmptyPhoneAndInstagram_PersistsNull()
    {
        await SeedStudio();
        UpdateStudioRequest req = new("New Name", "Porto", 41.1, -8.6, "", "  ");

        StudioResponse result = await CreateSut().Handle(new UpdateMyStudioCommand(req), default);

        result.PhoneNumber.Should().BeNull();
        result.InstagramHandle.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AddNipt_PersistsNormalizedNipt()
    {
        await SeedStudio();
        UpdateStudioRequest req = new("New Name", "Porto", 41.1, -8.6, Nipt: "l01234567a");

        StudioResponse result = await CreateSut().Handle(new UpdateMyStudioCommand(req), default);

        result.Nipt.Should().Be("L01234567A");
        _db.Studios.Single(s => s.Id == _studioId).Nipt.Should().Be("L01234567A");
    }

    [Fact]
    public async Task Handle_NullNipt_LeavesExistingNiptUnchanged()
    {
        Studio studio = new()
        {
            Id = _studioId,
            Name = "Old Name",
            Slug = "old-slug",
            City = "Lisbon",
            Nipt = "L01234567A",
        };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();

        UpdateStudioRequest req = new("New Name", "Porto", 41.1, -8.6, Nipt: null);

        StudioResponse result = await CreateSut().Handle(new UpdateMyStudioCommand(req), default);

        result.Nipt.Should().Be("L01234567A");
    }

    [Fact]
    public async Task Handle_DuplicateNiptDifferentOwner_ThrowsDuplicateNiptException()
    {
        await SeedStudio();
        _db.Studios.Add(new Studio
        {
            Name = "Other Studio",
            Slug = "other-studio",
            City = "Porto",
            OwnerEmail = "other-owner@example.com",
            Nipt = "L01234567A",
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        UpdateStudioRequest req = new("New Name", "Porto", 41.1, -8.6, Nipt: "L01234567A");

        Func<Task> act = () => CreateSut().Handle(new UpdateMyStudioCommand(req), default);

        await act.Should().ThrowAsync<DuplicateNiptException>();
    }

    [Fact]
    public async Task Handle_DuplicateNiptSameOwnerEmail_Succeeds()
    {
        Studio myStudio = new()
        {
            Id = _studioId,
            Name = "Old Name",
            Slug = "old-slug",
            City = "Lisbon",
            OwnerEmail = "owner@example.com",
            IsActive = true,
        };
        _db.Studios.Add(myStudio);
        _db.Studios.Add(new Studio
        {
            Name = "My Other Location",
            Slug = "other-location",
            City = "Porto",
            OwnerEmail = "owner@example.com",
            Nipt = "L01234567A",
            IsActive = true,
        });
        await _db.SaveChangesAsync();

        UpdateStudioRequest req = new("New Name", "Porto", 41.1, -8.6, Nipt: "L01234567A");

        StudioResponse result = await CreateSut().Handle(new UpdateMyStudioCommand(req), default);

        result.Nipt.Should().Be("L01234567A");
    }
}
