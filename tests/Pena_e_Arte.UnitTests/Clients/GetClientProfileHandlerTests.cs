using FluentAssertions;
using Pena_e_Arte.Application.Clients.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.ValueObjects;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class GetClientProfileHandlerTests
{
    private readonly FakeDbContext _db = FakeDbContext.Create();

    private GetClientProfileHandler CreateSut() => new(_db);

    [Fact]
    public async Task Handle_ExistingProfile_ReturnsProfileResponse()
    {
        Guid clientId = Guid.NewGuid();
        Guid studioId = Guid.NewGuid();
        _db.ClientProfiles.Add(new ClientProfile
        {
            ClientId     = clientId,
            StudioId     = studioId,
            DateOfBirth  = new DateOnly(1990, 5, 15),
            MedicalNotes = "None",
            Allergies    = "Latex",
            BodyMap      = new BodyMap { Locations = ["left_arm"] },
        });
        await _db.SaveChangesAsync();

        ClientProfileResponse? result = await CreateSut().Handle(new GetClientProfileQuery(clientId), default);

        result.Should().NotBeNull();
        result!.ClientId.Should().Be(clientId);
        result.StudioId.Should().Be(studioId);
        result.DateOfBirth.Should().Be(new DateOnly(1990, 5, 15));
        result.MedicalNotes.Should().Be("None");
        result.Allergies.Should().Be("Latex");
        result.BodyMapLocations.Should().ContainSingle("left_arm");
    }

    [Fact]
    public async Task Handle_MissingProfile_ReturnsNull()
    {
        ClientProfileResponse? result = await CreateSut().Handle(new GetClientProfileQuery(Guid.NewGuid()), default);

        result.Should().BeNull();
    }
}
