using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Clients.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class ExportMyDataHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly IR2Service _r2 = Substitute.For<IR2Service>();

    private ExportMyDataHandler CreateSut() => new(_db, _currentUser, _r2);

    [Fact]
    public async Task Handle_CallerBelongsToTwoStudios_ReturnsOneSectionPerStudio()
    {
        Guid userId = Guid.NewGuid();
        Guid studioA = await SeedStudioAsync("Studio A");
        Guid studioB = await SeedStudioAsync("Studio B");
        await SeedClientAsync(userId, studioA);
        await SeedClientAsync(userId, studioB);
        _currentUser.UserId.Returns(userId);

        ClientDataExportResponse result = await CreateSut().Handle(new ExportMyDataQuery(), default);

        result.Studios.Should().HaveCount(2);
        result.Studios.Should().Contain(s => s.StudioName == "Studio A");
        result.Studios.Should().Contain(s => s.StudioName == "Studio B");
    }

    [Fact]
    public async Task Handle_ExcludesOtherClientsData()
    {
        Guid userId = Guid.NewGuid();
        Guid studioA = await SeedStudioAsync("Studio A");
        Client mine = await SeedClientAsync(userId, studioA);
        Client other = await SeedClientAsync(Guid.NewGuid(), studioA, email: "other@example.com");

        TattooRecord otherTattoo = new()
        {
            StudioId = studioA,
            ClientId = other.Id,
            ArtistId = Guid.NewGuid(),
            Description = "Other's tattoo",
            BodyLocation = "Arm",
            CompletedAt = DateTime.UtcNow,
        };
        _db.TattooRecords.Add(otherTattoo);
        TattooRecord myTattoo = new()
        {
            StudioId = studioA,
            ClientId = mine.Id,
            ArtistId = Guid.NewGuid(),
            Description = "My tattoo",
            BodyLocation = "Leg",
            CompletedAt = DateTime.UtcNow,
        };
        _db.TattooRecords.Add(myTattoo);
        await _db.SaveChangesAsync();

        _currentUser.UserId.Returns(userId);

        ClientDataExportResponse result = await CreateSut().Handle(new ExportMyDataQuery(), default);

        result.Studios.Should().ContainSingle();
        result.Studios[0].TattooRecords.Should().ContainSingle(t => t.Description == "My tattoo");
    }

    [Fact]
    public async Task Handle_NoClientForCaller_ThrowsNotFound()
    {
        _currentUser.UserId.Returns(Guid.NewGuid());

        Func<Task> act = () => CreateSut().Handle(new ExportMyDataQuery(), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    private async Task<Guid> SeedStudioAsync(string name)
    {
        Studio studio = new() { Name = name };
        _db.Studios.Add(studio);
        await _db.SaveChangesAsync();
        return studio.Id;
    }

    private async Task<Client> SeedClientAsync(Guid userId, Guid studioId, string? email = null)
    {
        Client client = new()
        {
            StudioId = studioId,
            UserId = userId,
            FirstName = "Test",
            LastName = "Client",
            Email = email ?? $"{Guid.NewGuid()}@test.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return client;
    }
}
