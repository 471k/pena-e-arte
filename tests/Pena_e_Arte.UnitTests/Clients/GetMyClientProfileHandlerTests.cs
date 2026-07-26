using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Clients.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Domain.ValueObjects;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class GetMyClientProfileHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();
    private readonly ICurrentUser _currentUser = Substitute.For<ICurrentUser>();
    private readonly Guid _studioId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public GetMyClientProfileHandlerTests() =>
        _currentUser.UserId.Returns(_userId);

    private GetMyClientProfileHandler CreateSut() => new(_db, _currentUser);

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
    public async Task Handle_ExistingProfile_ReturnsProfileResponse()
    {
        Client client = await SeedClientAsync();
        _db.ClientProfiles.Add(new ClientProfile
        {
            StudioId = _studioId,
            ClientId = client.Id,
            DateOfBirth = new DateOnly(1990, 5, 15),
            MedicalNotes = "None",
            Allergies = "Latex",
            BodyMap = new BodyMap { Locations = ["left_arm"] },
        });
        await _db.SaveChangesAsync();

        ClientProfileResponse result = await CreateSut().Handle(new GetMyClientProfileQuery(), default);

        result.ClientId.Should().Be(client.Id);
        result.DateOfBirth.Should().Be(new DateOnly(1990, 5, 15));
        result.MedicalNotes.Should().Be("None");
        result.Allergies.Should().Be("Latex");
        result.BodyMapLocations.Should().ContainSingle("left_arm");
        result.AllowCrossTenantRead.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_OptedInProfile_ReturnsAllowCrossTenantReadTrue()
    {
        Client client = await SeedClientAsync();
        ClientProfile profile = new() { StudioId = _studioId, ClientId = client.Id };
        profile.OptInToCrossTenant();
        _db.ClientProfiles.Add(profile);
        await _db.SaveChangesAsync();

        ClientProfileResponse result = await CreateSut().Handle(new GetMyClientProfileQuery(), default);

        result.AllowCrossTenantRead.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_ClientNotFound_ThrowsNotFoundException()
    {
        Func<Task> act = () => CreateSut().Handle(new GetMyClientProfileQuery(), default);

        await act.Should().ThrowAsync<NotFoundException>();
    }

    [Fact]
    public async Task Handle_ProfileNotFound_ReturnsEmptyDefaultsInsteadOfThrowing()
    {
        Client client = await SeedClientAsync();

        ClientProfileResponse result = await CreateSut().Handle(new GetMyClientProfileQuery(), default);

        result.ClientId.Should().Be(client.Id);
        result.BodyMapLocations.Should().BeEmpty();
        result.AllowCrossTenantRead.Should().BeFalse();
        result.MedicalNotes.Should().BeNull();
        result.Allergies.Should().BeNull();
    }
}
