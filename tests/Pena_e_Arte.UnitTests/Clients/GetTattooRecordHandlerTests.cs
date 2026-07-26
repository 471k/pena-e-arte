using FluentAssertions;
using Pena_e_Arte.Application.Clients.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class GetTattooRecordHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetTattooRecordHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ExistingRecord_ReturnsTattooRecordResponse()
    {
        Guid clientId = Guid.NewGuid();
        TattooRecord record = new()
        {
            StudioId = Guid.NewGuid(),
            ClientId = clientId,
            ArtistId = Guid.NewGuid(),
            Description = "Dragon",
            BodyLocation = "left_arm",
            CompletedAt = DateTime.UtcNow.AddDays(-5),
        };
        _db.TattooRecords.Add(record);
        await _db.SaveChangesAsync();

        TattooRecordResponse result = await CreateSut()
            .Handle(new GetTattooRecordQuery(clientId, record.Id), default);

        result.Id.Should().Be(record.Id);
        result.ClientId.Should().Be(clientId);
        result.Description.Should().Be("Dragon");
        result.BodyLocation.Should().Be("left_arm");
    }

    [Fact]
    public async Task Handle_UnknownRecord_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut()
            .Handle(new GetTattooRecordQuery(Guid.NewGuid(), Guid.NewGuid()), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_RecordBelongingToDifferentClient_ThrowsNotFoundException()
    {
        TattooRecord record = new()
        {
            StudioId = Guid.NewGuid(),
            ClientId = Guid.NewGuid(),
            ArtistId = Guid.NewGuid(),
            Description = "Rose",
            BodyLocation = "wrist",
            CompletedAt = DateTime.UtcNow.AddDays(-3),
        };
        _db.TattooRecords.Add(record);
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut()
            .Handle(new GetTattooRecordQuery(Guid.NewGuid(), record.Id), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
