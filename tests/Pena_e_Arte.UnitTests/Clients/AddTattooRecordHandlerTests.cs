using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class AddTattooRecordHandlerTests
{
    private readonly FakeDbContext  _db       = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant   = Substitute.For<ICurrentTenant>();
    private readonly Guid           _studioId = Guid.NewGuid();

    public AddTattooRecordHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private AddTattooRecordHandler CreateSut() => new(_db, _tenant);

    private async Task<(Client client, Artist artist)> AddClientAndArtistAsync()
    {
        Client client = new() { StudioId = _studioId, FirstName = "Ana", LastName = "Costa", Email = "ana@example.com" };
        Artist artist = new() { StudioId = _studioId, FirstName = "Luis", LastName = "Silva", Email = "luis@example.com" };
        _db.Clients.Add(client);
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();
        return (client, artist);
    }

    [Fact]
    public async Task Handle_ValidRequest_ReturnsTattooRecordResponse()
    {
        (Client client, Artist artist) = await AddClientAndArtistAsync();
        DateTime completed = DateTime.UtcNow.AddDays(-10);
        AddTattooRecordRequest req = new(
            ArtistId:      artist.Id,
            AppointmentId: null,
            Description:   "Dragon sleeve",
            BodyLocation:  "left_arm",
            PhotoUrls:     ["https://r2.example.com/photo1.jpg"],
            CompletedAt:   completed);

        TattooRecordResponse result = await CreateSut()
            .Handle(new AddTattooRecordCommand(client.Id, req), default);

        result.ClientId.Should().Be(client.Id);
        result.ArtistId.Should().Be(artist.Id);
        result.Description.Should().Be("Dragon sleeve");
        result.BodyLocation.Should().Be("left_arm");
        result.PhotoUrls.Should().ContainSingle("https://r2.example.com/photo1.jpg");
        result.CompletedAt.Should().BeCloseTo(completed, TimeSpan.FromSeconds(1));
        result.Id.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Handle_ValidRequest_PersistsTattooRecord()
    {
        (Client client, Artist artist) = await AddClientAndArtistAsync();
        AddTattooRecordRequest req = new(
            ArtistId:      artist.Id,
            AppointmentId: null,
            Description:   "Rose",
            BodyLocation:  "right_wrist",
            PhotoUrls:     [],
            CompletedAt:   DateTime.UtcNow.AddDays(-5));

        await CreateSut().Handle(new AddTattooRecordCommand(client.Id, req), default);

        _db.TattooRecords.Should().ContainSingle(t => t.ClientId == client.Id && t.Description == "Rose");
    }

    [Fact]
    public async Task Handle_UnknownClient_ThrowsNotFoundException()
    {
        Artist artist = new() { StudioId = _studioId, FirstName = "Luis", LastName = "Silva", Email = "luis@example.com" };
        _db.Artists.Add(artist);
        await _db.SaveChangesAsync();

        AddTattooRecordRequest req = new(
            ArtistId:      artist.Id,
            AppointmentId: null,
            Description:   "Rose",
            BodyLocation:  "right_wrist",
            PhotoUrls:     [],
            CompletedAt:   DateTime.UtcNow.AddDays(-1));

        Func<Task> act = () => CreateSut()
            .Handle(new AddTattooRecordCommand(Guid.NewGuid(), req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_UnknownArtist_ThrowsNotFoundException()
    {
        Client client = new() { StudioId = _studioId, FirstName = "Ana", LastName = "Costa", Email = "ana@example.com" };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();

        AddTattooRecordRequest req = new(
            ArtistId:      Guid.NewGuid(),
            AppointmentId: null,
            Description:   "Rose",
            BodyLocation:  "right_wrist",
            PhotoUrls:     [],
            CompletedAt:   DateTime.UtcNow.AddDays(-1));

        Func<Task> act = () => CreateSut()
            .Handle(new AddTattooRecordCommand(client.Id, req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
