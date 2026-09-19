using FluentAssertions;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Contracts.Responses.Public;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Public;

public class GetPublicServicesHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetPublicServicesHandler CreateSut() => new(_db);

    private static Studio MakeStudio(string slug = "guest-studio") => new()
    {
        Name = "Guest Studio",
        Slug = slug,
        City = "Porto",
        IsActive = true,
        IsPublished = true,
    };

    [Fact]
    public async Task Handle_ActiveServices_ReturnsThemOrderedByName()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        _db.Services.Add(new Service { StudioId = studio.Id, Name = "Z Service", DurationMinutes = 60, IsActive = true });
        _db.Services.Add(new Service { StudioId = studio.Id, Name = "A Service", DurationMinutes = 30, IsActive = true });
        await _db.SaveChangesAsync();

        List<PublicServiceResponse> result = await CreateSut().Handle(new GetPublicServicesQuery(studio.Slug), default);

        result.Should().HaveCount(2);
        result[0].Name.Should().Be("A Service");
        result[1].Name.Should().Be("Z Service");
    }

    [Fact]
    public async Task Handle_InactiveService_IsExcluded()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        _db.Services.Add(new Service { StudioId = studio.Id, Name = "Retired", DurationMinutes = 60, IsActive = false });
        await _db.SaveChangesAsync();

        List<PublicServiceResponse> result = await CreateSut().Handle(new GetPublicServicesQuery(studio.Slug), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ServiceFromAnotherStudio_IsExcluded()
    {
        Studio studio = MakeStudio();
        _db.Studios.Add(studio);
        _db.Services.Add(new Service { StudioId = Guid.NewGuid(), Name = "Other Studio's Service", DurationMinutes = 60, IsActive = true });
        await _db.SaveChangesAsync();

        List<PublicServiceResponse> result = await CreateSut().Handle(new GetPublicServicesQuery(studio.Slug), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnknownSlug_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetPublicServicesQuery("no-such-slug"), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
