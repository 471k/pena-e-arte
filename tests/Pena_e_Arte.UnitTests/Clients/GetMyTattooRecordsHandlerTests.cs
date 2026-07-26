using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Clients.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class GetMyTattooRecordsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public GetMyTattooRecordsHandlerTests() =>
        _currentUser.UserId.Returns(_userId);

    private GetMyTattooRecordsHandler CreateSut() => new(_db, _currentUser);

    private async Task<Client> SeedClientAsync()
    {
        Client client = new()
        {
            StudioId = _studioId,
            UserId = _userId,
            FirstName = "Ana",
            LastName = "Costa",
            Email = "ana@example.com",
        };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return client;
    }

    [Fact]
    public async Task Handle_ClientWithRecords_ReturnsOrderedByCompletedAtDescending()
    {
        Client client = await SeedClientAsync();
        Guid artistId = Guid.NewGuid();
        DateTime older = DateTime.UtcNow.AddDays(-20);
        DateTime newer = DateTime.UtcNow.AddDays(-5);

        _db.TattooRecords.AddRange(
            new TattooRecord { StudioId = _studioId, ClientId = client.Id, ArtistId = artistId, Description = "Rose", BodyLocation = "wrist", CompletedAt = older },
            new TattooRecord { StudioId = _studioId, ClientId = client.Id, ArtistId = artistId, Description = "Dragon", BodyLocation = "left_arm", CompletedAt = newer });
        await _db.SaveChangesAsync();

        List<TattooRecordResponse> result = await CreateSut().Handle(new GetMyTattooRecordsQuery(), default);

        result.Should().HaveCount(2);
        result[0].Description.Should().Be("Dragon");
        result[1].Description.Should().Be("Rose");
    }

    [Fact]
    public async Task Handle_ClientWithNoRecords_ReturnsEmptyList()
    {
        await SeedClientAsync();

        List<TattooRecordResponse> result = await CreateSut().Handle(new GetMyTattooRecordsQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_OtherClientRecordsNotReturned()
    {
        Client client = await SeedClientAsync();
        Guid otherClientId = Guid.NewGuid();
        Guid artistId = Guid.NewGuid();

        _db.TattooRecords.Add(new TattooRecord
        {
            StudioId = _studioId,
            ClientId = otherClientId,
            ArtistId = artistId,
            Description = "Other client",
            BodyLocation = "back",
            CompletedAt = DateTime.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();

        List<TattooRecordResponse> result = await CreateSut().Handle(new GetMyTattooRecordsQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ClientNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetMyTattooRecordsQuery(), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
