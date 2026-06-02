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

public class UpsertClientProfileHandlerTests
{
    private readonly FakeDbContext  _db       = FakeDbContext.Create();
    private readonly ICurrentTenant _tenant   = Substitute.For<ICurrentTenant>();
    private readonly Guid           _studioId = Guid.NewGuid();

    public UpsertClientProfileHandlerTests() =>
        _tenant.StudioId.Returns(_studioId);

    private UpsertClientProfileHandler CreateSut() => new(_db, _tenant);

    private async Task<Client> AddClientAsync()
    {
        Client client = new() { StudioId = _studioId, FirstName = "Ana", LastName = "Costa", Email = "ana@example.com" };
        _db.Clients.Add(client);
        await _db.SaveChangesAsync();
        return client;
    }

    [Fact]
    public async Task Handle_NoExistingProfile_CreatesAndReturnsProfile()
    {
        Client client = await AddClientAsync();
        UpsertClientProfileRequest req = new(new DateOnly(1990, 1, 1), "None", "Latex");

        ClientProfileResponse result = await CreateSut()
            .Handle(new UpsertClientProfileCommand(client.Id, req), default);

        result.ClientId.Should().Be(client.Id);
        result.StudioId.Should().Be(_studioId);
        result.DateOfBirth.Should().Be(new DateOnly(1990, 1, 1));
        result.MedicalNotes.Should().Be("None");
        result.Allergies.Should().Be("Latex");
        _db.ClientProfiles.Should().ContainSingle(p => p.ClientId == client.Id);
    }

    [Fact]
    public async Task Handle_ExistingProfile_UpdatesProfile()
    {
        Client client = await AddClientAsync();
        _db.ClientProfiles.Add(new ClientProfile
        {
            StudioId     = _studioId,
            ClientId     = client.Id,
            MedicalNotes = "Old notes",
        });
        await _db.SaveChangesAsync();

        UpsertClientProfileRequest req = new(new DateOnly(1985, 6, 20), "New notes", null);
        ClientProfileResponse result = await CreateSut()
            .Handle(new UpsertClientProfileCommand(client.Id, req), default);

        result.MedicalNotes.Should().Be("New notes");
        result.DateOfBirth.Should().Be(new DateOnly(1985, 6, 20));
        result.Allergies.Should().BeNull();
        _db.ClientProfiles.Should().ContainSingle(p => p.ClientId == client.Id);
    }

    [Fact]
    public async Task Handle_UnknownClient_ThrowsNotFoundException()
    {
        UpsertClientProfileRequest req = new(null, null, null);

        Func<Task> act = () => CreateSut()
            .Handle(new UpsertClientProfileCommand(Guid.NewGuid(), req), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
