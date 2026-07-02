using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class UpdateMyStudioHandlerTests
{
    private readonly FakeDbContext  _db       = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant   = Substitute.For<ICurrentTenant>();
    private readonly Guid           _studioId = Guid.NewGuid();

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
}
