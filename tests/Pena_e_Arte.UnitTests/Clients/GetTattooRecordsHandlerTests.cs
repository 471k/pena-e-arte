using FluentAssertions;
using Pena_e_Arte.Application.Clients.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class GetTattooRecordsHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetTattooRecordsHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ClientWithRecords_ReturnsOrderedByCompletedAtDescending()
    {
        Guid clientId = Guid.NewGuid();
        Guid studioId = Guid.NewGuid();
        Guid artistId = Guid.NewGuid();
        DateTime older = DateTime.UtcNow.AddDays(-20);
        DateTime newer = DateTime.UtcNow.AddDays(-5);

        _db.TattooRecords.AddRange(
            new TattooRecord { StudioId = studioId, ClientId = clientId, ArtistId = artistId, Description = "Rose",   BodyLocation = "wrist",    CompletedAt = older },
            new TattooRecord { StudioId = studioId, ClientId = clientId, ArtistId = artistId, Description = "Dragon", BodyLocation = "left_arm", CompletedAt = newer });
        await _db.SaveChangesAsync();

        List<TattooRecordResponse> result = await CreateSut().Handle(new GetTattooRecordsQuery(clientId), default);

        result.Should().HaveCount(2);
        result[0].Description.Should().Be("Dragon");
        result[1].Description.Should().Be("Rose");
    }

    [Fact]
    public async Task Handle_ClientWithNoRecords_ReturnsEmptyList()
    {
        List<TattooRecordResponse> result = await CreateSut().Handle(new GetTattooRecordsQuery(Guid.NewGuid()), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_OtherClientRecordsNotReturned()
    {
        Guid clientId      = Guid.NewGuid();
        Guid otherClientId = Guid.NewGuid();
        Guid studioId      = Guid.NewGuid();
        Guid artistId      = Guid.NewGuid();

        _db.TattooRecords.Add(new TattooRecord
        {
            StudioId     = studioId,
            ClientId     = otherClientId,
            ArtistId     = artistId,
            Description  = "Other client",
            BodyLocation = "back",
            CompletedAt  = DateTime.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();

        List<TattooRecordResponse> result = await CreateSut().Handle(new GetTattooRecordsQuery(clientId), default);

        result.Should().BeEmpty();
    }
}
