using FluentValidation.TestHelper;
using NSubstitute;
using Pena_e_Arte.Application.IntakeForms.Commands;
using Pena_e_Arte.Application.IntakeForms.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.UnitTests.IntakeForms;

public class SubmitIntakeFormValidatorTests
{
    private const string ValidUrl = "https://cdn.example.com/intake.pdf";

    private readonly IR2Service _r2 = Substitute.For<IR2Service>();
    private readonly SubmitIntakeFormValidator _validator;

    public SubmitIntakeFormValidatorTests()
    {
        _r2.IsR2Url(ValidUrl).Returns(true);
        _validator = new SubmitIntakeFormValidator(_r2);
    }

    private static SubmitIntakeFormCommand ValidCommand() =>
        new(new SubmitIntakeFormRequest(Guid.NewGuid(), null, "{\"allergies\":\"none\"}", null));

    [Fact]
    public void Validate_ValidCommand_NoErrors()
    {
        _validator.TestValidate(ValidCommand()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Validate_ValidCommandWithFileUrl_NoErrors()
    {
        SubmitIntakeFormCommand cmd = new(new SubmitIntakeFormRequest(
            Guid.NewGuid(), null, "{}", ValidUrl));
        _validator.TestValidate(cmd).ShouldNotHaveAnyValidationErrors();
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
    public void Validate_FileUrlNotFromR2_HasError()
    {
        const string externalUrl = "https://external.attacker.com/evil.pdf";
        _r2.IsR2Url(externalUrl).Returns(false);

        SubmitIntakeFormCommand cmd = new(new SubmitIntakeFormRequest(
            Guid.NewGuid(), null, "{}", externalUrl));
        _validator.TestValidate(cmd).ShouldHaveValidationErrorFor(x => x.Request.FileUrl);
    }

    [Fact]
    public void Validate_NullFileUrl_NoError()
    {
        _validator.TestValidate(ValidCommand()).ShouldNotHaveValidationErrorFor(x => x.Request.FileUrl);
    }
}
