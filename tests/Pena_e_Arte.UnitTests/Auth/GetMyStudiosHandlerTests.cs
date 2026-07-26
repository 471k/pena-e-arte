using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Auth.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Auth;

public class GetMyStudiosHandlerTests
{
    private readonly IIdentityService _identity = Substitute.For<IIdentityService>();
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly FakeCurrentUser _currentUser = FakeCurrentUser.Client();

    private GetMyStudiosHandler CreateSut() => new(_db, _identity, _currentUser);

    private void UserHasTenantIds(params Guid[] studioIds) =>
        _identity.GetTenantIdsAsync(_currentUser.UserId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<Guid>)studioIds);

    [Fact]
    public async Task Handle_NoTenantClaims_ReturnsEmptyList()
    {
        UserHasTenantIds();

        List<MyStudioResponse> result = await CreateSut().Handle(new GetMyStudiosQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_MultipleStudios_ReturnsOrderedByName()
    {
        Studio zeta = new() { Id = Guid.NewGuid(), Name = "Zeta Ink", Slug = "zeta-ink", City = "Tirana" };
        Studio alpha = new() { Id = Guid.NewGuid(), Name = "Alpha Art", Slug = "alpha-art", City = "Durrës" };
        _db.Studios.AddRange(zeta, alpha);
        await _db.SaveChangesAsync();
        UserHasTenantIds(zeta.Id, alpha.Id);

        List<MyStudioResponse> result = await CreateSut().Handle(new GetMyStudiosQuery(), default);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("Alpha Art");
        result[1].Name.Should().Be("Zeta Ink");
    }

    [Fact]
    public async Task Handle_SingleStudio_ReturnsCorrectFieldValues()
    {
        Studio studio = new()
        {
            Id = Guid.NewGuid(),
            Name = "Alpha Art",
            Slug = "alpha-art",
            City = "Durrës",
            CoverImageUrl = "https://cdn.example.com/cover.jpg",
            IsActive = true,
        };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        UserHasTenantIds(studio.Id);

        List<MyStudioResponse> result = await CreateSut().Handle(new GetMyStudiosQuery(), default);

        result.Should().ContainSingle();
        MyStudioResponse response = result[0];
        response.StudioId.Should().Be(studio.Id);
        response.Name.Should().Be("Alpha Art");
        response.Slug.Should().Be("alpha-art");
        response.City.Should().Be("Durrës");
        response.CoverImageUrl.Should().Be("https://cdn.example.com/cover.jpg");
    }

    [Fact]
    public async Task Handle_StudioIsActive_ReturnsIsStudioActiveTrue()
    {
        Studio studio = new() { Id = Guid.NewGuid(), Name = "Alpha Art", Slug = "alpha-art", City = "Durrës", IsActive = true };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        UserHasTenantIds(studio.Id);

        List<MyStudioResponse> result = await CreateSut().Handle(new GetMyStudiosQuery(), default);

        result[0].IsStudioActive.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_StudioIsSuspended_ReturnsIsStudioActiveFalse()
    {
        Studio studio = new() { Id = Guid.NewGuid(), Name = "Closed Ink", Slug = "closed-ink", City = "Vlorë", IsActive = false };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        UserHasTenantIds(studio.Id);

        List<MyStudioResponse> result = await CreateSut().Handle(new GetMyStudiosQuery(), default);

        result[0].IsStudioActive.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_UserHasNoClaimForAStudio_DoesNotReturnThatStudio()
    {
        Studio claimed = new() { Id = Guid.NewGuid(), Name = "Alpha Art", Slug = "alpha-art", City = "Durrës" };
        Studio unclaimed = new() { Id = Guid.NewGuid(), Name = "Other Ink", Slug = "other-ink", City = "Tirana" };
        _db.Studios.AddRange(claimed, unclaimed);
        await _db.SaveChangesAsync();
        UserHasTenantIds(claimed.Id);

        List<MyStudioResponse> result = await CreateSut().Handle(new GetMyStudiosQuery(), default);

        result.Should().ContainSingle();
        result[0].StudioId.Should().Be(claimed.Id);
    }

    [Fact]
    public async Task Handle_ClientBelongsToThreeStudios_ReturnsAllThree()
    {
        Studio a = new() { Id = Guid.NewGuid(), Name = "A Studio", Slug = "a-studio", City = "Tirana" };
        Studio b = new() { Id = Guid.NewGuid(), Name = "B Studio", Slug = "b-studio", City = "Durrës" };
        Studio c = new() { Id = Guid.NewGuid(), Name = "C Studio", Slug = "c-studio", City = "Vlorë" };
        _db.Studios.AddRange(a, b, c);
        await _db.SaveChangesAsync();
        UserHasTenantIds(a.Id, b.Id, c.Id);

        List<MyStudioResponse> result = await CreateSut().Handle(new GetMyStudiosQuery(), default);

        result.Should().HaveCount(3);
        result.Select(r => r.StudioId).Should().BeEquivalentTo([a.Id, b.Id, c.Id]);
    }
}
