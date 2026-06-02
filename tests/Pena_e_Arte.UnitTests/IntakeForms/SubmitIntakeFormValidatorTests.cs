using FluentValidation.TestHelper;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Application.IntakeForms.Validators;
using Pena_e_Arte.Contracts.Requests;

namespace Pena_e_Arte.UnitTests.IntakeForms;

public class SubmitIntakeFormValidatorTests
{
    private readonly SubmitIntakeFormValidator _validator = new();

    private static SubmitIntakeFormCommand ValidCommand() =>
        new(new SubmitIntakeFormRequest(Guid.NewGuid(), null, "{\"allergies\":\"none\"}", null));

    [Fact]
    public void Validate_ValidCommand_NoErrors()
    {
        _validator.TestValidate(ValidCommand()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyClientId_HasError()
    {
        SubmitIntakeFormCommand cmd = new(new SubmitIntakeFormRequest(Guid.Empty, null, "{}", null));
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Request.ClientId);
    }

    [Fact]
    public void Validate_EmptyFormData_HasError()
    {
        SubmitIntakeFormCommand cmd = new(new SubmitIntakeFormRequest(Guid.NewGuid(), null, "", null));
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Request.FormData);
    }

    [Fact]
    public void Validate_FormDataExceedsMaxLength_HasError()
    {
        string tooLong = new('x', 65536);
        SubmitIntakeFormCommand cmd = new(new SubmitIntakeFormRequest(Guid.NewGuid(), null, tooLong, null));
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Request.FormData);
    }

    [Fact]
    public void Validate_FileUrlExceedsMaxLength_HasError()
    {
        string tooLong = new('x', 1001);
        SubmitIntakeFormCommand cmd = new(new SubmitIntakeFormRequest(Guid.NewGuid(), null, "{}", tooLong));
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Request.FileUrl);
    }

    [Fact]
    public void Validate_NullFileUrl_NoError()
    {
        _validator.TestValidate(ValidCommand()).ShouldNotHaveValidationErrorFor(x => x.Request.FileUrl);
    }
}
