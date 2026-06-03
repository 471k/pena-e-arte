using FluentAssertions;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class UpdateTattooRecordHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private UpdateTattooRecordHandler CreateSut() => new(_db);

    private async Task<TattooRecord> SeedRecordAsync(Guid clientId)
    {
        TattooRecord record = new()
        {
            StudioId     = Guid.NewGuid(),
            ClientId     = clientId,
            ArtistId     = Guid.NewGuid(),
            Description  = "Original",
            BodyLocation = "left_arm",
            PhotoUrls    = [],
            CompletedAt  = DateTime.UtcNow.AddDays(-10),
        };
        _db.TattooRecords.Add(record);
        await _db.SaveChangesAsync();
        return record;
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsUpdatedResponse()
    {
        Guid clientId = Guid.NewGuid();
        TattooRecord record = await SeedRecordAsync(clientId);
        DateTime newDate = DateTime.UtcNow.AddDays(-3);
        UpdateTattooRecordRequest req = new("Updated desc", "right_leg", ["https://r2.example.com/new.jpg"], newDate);

        TattooRecordResponse result = await CreateSut()
            .Handle(new UpdateTattooRecordCommand(clientId, record.Id, req), default);

        result.Description.Should().Be("Updated desc");
        result.BodyLocation.Should().Be("right_leg");
        result.PhotoUrls.Should().ContainSingle("https://r2.example.com/new.jpg");
        result.CompletedAt.Should().BeCloseTo(newDate, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsChanges()
    {
        Guid clientId = Guid.NewGuid();
        TattooRecord record = await SeedRecordAsync(clientId);
        UpdateTattooRecordRequest req = new("Changed", "back", [], DateTime.UtcNow.AddDays(-2));

        await CreateSut().Handle(new UpdateTattooRecordCommand(clientId, record.Id, req), default);

        TattooRecord updated = _db.TattooRecords.Single(t => t.Id == record.Id);
        updated.Description.Should().Be("Changed");
        updated.BodyLocation.Should().Be("back");
    }

    [Fact]
    public async Task Handle_UnknownRecord_ThrowsNotFoundException()
    {
        UpdateTattooRecordRequest req = new("desc", "arm", [], DateTime.UtcNow.AddDays(-1));

        Func<Task> act = () => CreateSut()
            .Handle(new UpdateTattooRecordCommand(Guid.NewGuid(), Guid.NewGuid(), req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_RecordBelongingToDifferentClient_ThrowsNotFoundException()
    {
        TattooRecord record = await SeedRecordAsync(Guid.NewGuid());
        UpdateTattooRecordRequest req = new("desc", "arm", [], DateTime.UtcNow.AddDays(-1));

        Func<Task> act = () => CreateSut()
            .Handle(new UpdateTattooRecordCommand(Guid.NewGuid(), record.Id, req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
