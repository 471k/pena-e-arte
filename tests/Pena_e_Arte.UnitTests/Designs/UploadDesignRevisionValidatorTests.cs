using FluentAssertions;
using FluentValidation.Results;
using NSubstitute;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Application.Designs.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class UploadDesignRevisionValidatorTests
{
    private const string ValidUrl = "https://cdn.example.com/v1.png";

    private readonly IR2Service _r2 = Substitute.For<IR2Service>();
    private readonly UploadDesignRevisionValidator _sut;

    public UploadDesignRevisionValidatorTests()
    {
        _r2.IsR2Url(ValidUrl).Returns(true);
        _sut = new UploadDesignRevisionValidator(_r2);
    }

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(new UploadDesignRevisionCommand(
            new UploadDesignRevisionRequest(Guid.NewGuid(), ValidUrl, null)));
    }

    [Fact]
    public void Validate_EmptyDesignId_FailsOnDesignId()
    {
        _sut.ShouldFailOn(
            new UploadDesignRevisionCommand(new UploadDesignRevisionRequest(Guid.Empty, ValidUrl, null)),
            "Request.DesignId");
    }

    [Fact]
    public void Validate_EmptyFileUrl_FailsOnFileUrl()
    {
        _sut.ShouldFailOn(
            new UploadDesignRevisionCommand(new UploadDesignRevisionRequest(Guid.NewGuid(), "", null)),
            "Request.FileUrl");
    }

    [Fact]
    public void Validate_FileUrlExceedsMaxLength_FailsOnFileUrl()
    {
        _sut.ShouldFailOn(
            new UploadDesignRevisionCommand(new UploadDesignRevisionRequest(Guid.NewGuid(), new('x', 1001), null)),
            "Request.FileUrl");
    }

    [Fact]
    public void Validate_FileUrlNotFromR2_FailsOnFileUrl()
    {
        _r2.IsR2Url("https://external.attacker.com/evil.png").Returns(false);

        _sut.ShouldFailOn(
            new UploadDesignRevisionCommand(new UploadDesignRevisionRequest(
                Guid.NewGuid(), "https://external.attacker.com/evil.png", null)),
            "Request.FileUrl");
    }

    [Fact]
    public void Validate_NullNotes_IsValid()
    {
        ValidationResult result = _sut.Validate(
            new UploadDesignRevisionCommand(new UploadDesignRevisionRequest(Guid.NewGuid(), ValidUrl, null)));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Notes");
    }

    [Fact]
    public void Validate_NotesExceedsMaxLength_FailsOnNotes()
    {
        _sut.ShouldFailOn(
            new UploadDesignRevisionCommand(new UploadDesignRevisionRequest(Guid.NewGuid(), ValidUrl, new('x', 2001))),
            "Request.Notes");
    }

    [Fact]
    public void Validate_NotesAtMaxLength_IsValid()
    {
        ValidationResult result = _sut.Validate(
            new UploadDesignRevisionCommand(new UploadDesignRevisionRequest(Guid.NewGuid(), ValidUrl, new('x', 2000))));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Notes");
    }
}
