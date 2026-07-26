using FluentAssertions;
using Pena_e_Arte.Application.Studios.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class GetStudioMapHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetStudioMapHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_OnlyActiveStudios_ReturnsAll()
    {
        await SeedStudios(active: 2, inactive: 0);

        List<StudioMapItemResponse> result = await CreateSut().Handle(new GetStudioMapQuery(), default);

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task Handle_MixedActiveAndInactive_ReturnsOnlyActive()
    {
        await SeedStudios(active: 2, inactive: 3);

        List<StudioMapItemResponse> result = await CreateSut().Handle(new GetStudioMapQuery(), default);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(s => s.Id.Should().NotBeEmpty());
    }

    [Fact]
    public async Task Handle_NoActiveStudios_ReturnsEmpty()
    {
        await SeedStudios(active: 0, inactive: 2);

        List<StudioMapItemResponse> result = await CreateSut().Handle(new GetStudioMapQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_EmptyDb_ReturnsEmpty()
    {
        List<StudioMapItemResponse> result = await CreateSut().Handle(new GetStudioMapQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ResponseContainsExpectedFields()
    {
        _db.Studios.Add(new Studio
        {
            Name = "Tinta Viva",
            Slug = "tinta-viva",
            City = "Lisboa",
            Latitude = 38.72,
            Longitude = -9.14,
            IsActive = true
        });
        await _db.SaveChangesAsync();

        List<StudioMapItemResponse> result = await CreateSut().Handle(new GetStudioMapQuery(), default);

        StudioMapItemResponse item = result.Single();
        item.Name.Should().Be("Tinta Viva");
        item.Slug.Should().Be("tinta-viva");
        item.City.Should().Be("Lisboa");
        item.Latitude.Should().Be(38.72);
        item.Longitude.Should().Be(-9.14);
    }

    private async Task SeedStudios(int active, int inactive)
    {
        for (int i = 0; i < active; i++)
            _db.Studios.Add(new Studio { Name = $"Active {i}", Slug = $"active-{i}", City = "Porto", IsActive = true });

        for (int i = 0; i < inactive; i++)
            _db.Studios.Add(new Studio { Name = $"Inactive {i}", Slug = $"inactive-{i}", City = "Porto", IsActive = false });

        await _db.SaveChangesAsync();
    }
}
