using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Clients.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class UpdateClientArtistValidatorTests
{
    private readonly UpdateClientArtistValidator _sut = new();

    [Fact]
    public void Validate_ValidReassignment_IsValid()
    {
        _sut.ShouldBeValid(new UpdateClientArtistCommand(Guid.NewGuid(), new UpdateClientArtistRequest(Guid.NewGuid())));
    }

    [Fact]
    public void Validate_NullArtistId_IsValid()
    {
        _sut.ShouldBeValid(new UpdateClientArtistCommand(Guid.NewGuid(), new UpdateClientArtistRequest(null)));
    }

    [Fact]
    public void Validate_EmptyClientId_FailsOnClientId()
    {
        _sut.ShouldFailOn(
            new UpdateClientArtistCommand(Guid.Empty, new UpdateClientArtistRequest(Guid.NewGuid())),
            "ClientId");
    }

    [Fact]
    public void Validate_EmptyArtistIdGuid_FailsOnArtistId()
    {
        _sut.ShouldFailOn(
            new UpdateClientArtistCommand(Guid.NewGuid(), new UpdateClientArtistRequest(Guid.Empty)),
            "Request.ArtistId");
    }
}
