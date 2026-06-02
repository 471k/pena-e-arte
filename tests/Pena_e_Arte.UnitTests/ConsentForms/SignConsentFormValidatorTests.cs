using FluentValidation.TestHelper;
using Pena_e_Arte.Application.ConsentForms.Commands;
using Pena_e_Arte.Application.ConsentForms.Validators;
using Pena_e_Arte.Contracts.Requests;

namespace Pena_e_Arte.UnitTests.ConsentForms;

public class SignConsentFormValidatorTests
{
    private readonly SignConsentFormValidator _validator = new();

    private static SignConsentFormCommand ValidCommand() =>
        new(new SignConsentFormRequest(Guid.NewGuid(), Guid.NewGuid(), "data:image/png;base64,abc123", null));

    [Fact]
    public void Validate_ValidCommand_NoErrors()
    {
        _validator.TestValidate(ValidCommand()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_EmptyClientId_HasError()
    {
        SignConsentFormCommand cmd = new(new SignConsentFormRequest(Guid.Empty, Guid.NewGuid(), "sig", null));
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Request.ClientId);
    }

    [Fact]
    public void Validate_EmptyAppointmentId_HasError()
    {
        SignConsentFormCommand cmd = new(new SignConsentFormRequest(Guid.NewGuid(), Guid.Empty, "sig", null));
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Request.AppointmentId);
    }

    [Fact]
    public void Validate_EmptySignatureData_HasError()
    {
        SignConsentFormCommand cmd = new(new SignConsentFormRequest(Guid.NewGuid(), Guid.NewGuid(), "", null));
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Request.SignatureData);
    }

    [Fact]
    public void Validate_SignatureDataExceedsMaxLength_HasError()
    {
        string tooLong = new('x', 5001);
        SignConsentFormCommand cmd = new(new SignConsentFormRequest(Guid.NewGuid(), Guid.NewGuid(), tooLong, null));
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Request.SignatureData);
    }

    [Fact]
    public void Validate_FileUrlExceedsMaxLength_HasError()
    {
        string tooLong = new('x', 1001);
        SignConsentFormCommand cmd = new(new SignConsentFormRequest(Guid.NewGuid(), Guid.NewGuid(), "sig", tooLong));
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Request.FileUrl);
    }

    [Fact]
    public void Validate_NullFileUrl_NoError()
    {
        _validator.TestValidate(ValidCommand()).ShouldNotHaveValidationErrorFor(x => x.Request.FileUrl);
    }
}
