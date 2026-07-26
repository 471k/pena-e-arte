using FluentAssertions;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class DeleteTattooRecordHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private DeleteTattooRecordHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ExistingRecord_SetsDeletedAt()
    {
        Guid clientId = Guid.NewGuid();
        TattooRecord record = new()
        {
            StudioId = Guid.NewGuid(),
            ClientId = clientId,
            ArtistId = Guid.NewGuid(),
            Description = "Dragon",
            BodyLocation = "left_arm",
            CompletedAt = DateTime.UtcNow.AddDays(-10),
        };
        _db.TattooRecords.Add(record);
        await _db.SaveChangesAsync();

        await CreateSut().Handle(new DeleteTattooRecordCommand(clientId, record.Id), default);

        _db.TattooRecords.Should().ContainSingle(t => t.Id == record.Id && t.DeletedAt != null);
    }

    [Fact]
    public async Task Handle_UnknownRecord_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut()
            .Handle(new DeleteTattooRecordCommand(Guid.NewGuid(), Guid.NewGuid()), default);

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
            Description = "Dragon",
            BodyLocation = "left_arm",
            CompletedAt = DateTime.UtcNow.AddDays(-10),
        };
        _db.TattooRecords.Add(record);
        await _db.SaveChangesAsync();

        Func<Task> act = () => CreateSut()
            .Handle(new DeleteTattooRecordCommand(Guid.NewGuid(), record.Id), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
