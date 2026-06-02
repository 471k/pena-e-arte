using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Clients.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class UpdateBodyMapValidatorTests
{
    private readonly UpdateBodyMapValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(Command(["left_arm", "right_shoulder"]));
    }

    [Fact]
    public void Validate_EmptyLocations_IsValid()
    {
        _sut.ShouldBeValid(Command([]));
    }

    [Fact]
    public void Validate_EmptyClientId_FailsOnClientId()
    {
        UpdateBodyMapCommand cmd = new(Guid.Empty, new UpdateBodyMapRequest([]));
        _sut.ShouldFailOn(cmd, "ClientId");
    }

    [Fact]
    public void Validate_LocationExceedsMaxLength_FailsOnLocations()
    {
        _sut.ShouldFailOn(Command([new('x', 201)]), "Request.Locations[0]");
    }

    [Fact]
    public void Validate_EmptyLocationString_FailsOnLocations()
    {
        _sut.ShouldFailOn(Command([""]), "Request.Locations[0]");
    }

    private static UpdateBodyMapCommand Command(List<string> locations) =>
        new(Guid.NewGuid(), new UpdateBodyMapRequest(locations));
}
