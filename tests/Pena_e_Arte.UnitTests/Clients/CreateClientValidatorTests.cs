using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Clients.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class CreateClientValidatorTests
{
    private readonly CreateClientValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(ValidCommand());
    }

    [Fact]
    public void Validate_EmptyFirstName_FailsOnFirstName()
    {
        _sut.ShouldFailOn(Command("", "Costa", "a@b.com", null), "Request.FirstName");
    }

    [Fact]
    public void Validate_FirstNameExceedsMaxLength_FailsOnFirstName()
    {
        _sut.ShouldFailOn(Command(new('x', 101), "Costa", "a@b.com", null), "Request.FirstName");
    }

    [Fact]
    public void Validate_EmptyLastName_FailsOnLastName()
    {
        _sut.ShouldFailOn(Command("Ana", "", "a@b.com", null), "Request.LastName");
    }

    [Fact]
    public void Validate_LastNameExceedsMaxLength_FailsOnLastName()
    {
        _sut.ShouldFailOn(Command("Ana", new('x', 101), "a@b.com", null), "Request.LastName");
    }

    [Fact]
    public void Validate_EmptyEmail_FailsOnEmail()
    {
        _sut.ShouldFailOn(Command("Ana", "Costa", "", null), "Request.Email");
    }

    [Fact]
    public void Validate_InvalidEmailFormat_FailsOnEmail()
    {
        _sut.ShouldFailOn(Command("Ana", "Costa", "not-an-email", null), "Request.Email");
    }

    [Fact]
    public void Validate_NullPhone_IsValid()
    {
        _sut.ShouldBeValid(Command("Ana", "Costa", "a@b.com", null));
    }

    [Fact]
    public void Validate_PhoneExceedsMaxLength_FailsOnPhone()
    {
        _sut.ShouldFailOn(Command("Ana", "Costa", "a@b.com", new('9', 21)), "Request.Phone");
    }

    [Fact]
    public void Validate_PhoneAtMaxLength_IsValid()
    {
        _sut.ShouldBeValid(Command("Ana", "Costa", "a@b.com", new('9', 20)));
    }

    private static CreateClientCommand ValidCommand() =>
        Command("Ana", "Costa", "ana@example.com", "+351911000000");

    private static CreateClientCommand Command(string first, string last, string email, string? phone) =>
        new(new CreateClientRequest(first, last, email, phone));
}
