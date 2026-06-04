using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Application.Artists.Validators;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class DeleteArtistValidatorTests
{
    private readonly DeleteArtistValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(new DeleteArtistCommand(Guid.NewGuid()));
    }

    [Fact]
    public void Validate_EmptyId_FailsOnId()
    {
        _sut.ShouldFailOn(new DeleteArtistCommand(Guid.Empty), "Id");
    }
}
