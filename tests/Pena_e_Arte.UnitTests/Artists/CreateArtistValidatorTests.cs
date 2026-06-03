using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Application.Artists.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class CreateArtistValidatorTests
{
    private readonly CreateArtistValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(ValidCommand());
    }

    [Fact]
    public void Validate_EmptyFirstName_FailsOnFirstName()
    {
        _sut.ShouldFailOn(Command("", "Tavares", "rui@studio.com", null), "Request.FirstName");
    }

    [Fact]
    public void Validate_FirstNameExceedsMaxLength_FailsOnFirstName()
    {
        _sut.ShouldFailOn(Command(new('x', 101), "Tavares", "rui@studio.com", null), "Request.FirstName");
    }

    [Fact]
    public void Validate_EmptyLastName_FailsOnLastName()
    {
        _sut.ShouldFailOn(Command("Rui", "", "rui@studio.com", null), "Request.LastName");
    }

    [Fact]
    public void Validate_LastNameExceedsMaxLength_FailsOnLastName()
    {
        _sut.ShouldFailOn(Command("Rui", new('x', 101), "rui@studio.com", null), "Request.LastName");
    }

    [Fact]
    public void Validate_EmptyEmail_FailsOnEmail()
    {
        _sut.ShouldFailOn(Command("Rui", "Tavares", "", null), "Request.Email");
    }

    [Fact]
    public void Validate_InvalidEmailFormat_FailsOnEmail()
    {
        _sut.ShouldFailOn(Command("Rui", "Tavares", "not-an-email", null), "Request.Email");
    }

    [Fact]
    public void Validate_NullSpecializations_IsValid()
    {
        _sut.ShouldBeValid(Command("Rui", "Tavares", "rui@studio.com", null));
    }

    [Fact]
    public void Validate_SpecializationsExceedsMaxLength_FailsOnSpecializations()
    {
        _sut.ShouldFailOn(Command("Rui", "Tavares", "rui@studio.com", new('x', 1001)), "Request.Specializations");
    }

    [Fact]
    public void Validate_SpecializationsAtMaxLength_IsValid()
    {
        _sut.ShouldBeValid(Command("Rui", "Tavares", "rui@studio.com", new('x', 1000)));
    }

    private static CreateArtistCommand ValidCommand() =>
        Command("Rui", "Tavares", "rui@studio.com", "Neo-traditional");

    private static CreateArtistCommand Command(string first, string last, string email, string? specializations) =>
        new(new CreateArtistRequest(first, last, email, specializations));
}
