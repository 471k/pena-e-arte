using FluentAssertions;
using FluentValidation.Results;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Application.Designs.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class UploadDesignRevisionValidatorTests
{
    private readonly UploadDesignRevisionValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(new UploadDesignRevisionCommand(
            new UploadDesignRevisionRequest(Guid.NewGuid(), "https://r2.example.com/v1.png", null)));
    }

    [Fact]
    public void Validate_EmptyDesignId_FailsOnDesignId()
    {
        _sut.ShouldFailOn(
            new UploadDesignRevisionCommand(new UploadDesignRevisionRequest(Guid.Empty, "https://r2.example.com/v1.png", null)),
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
    public void Validate_NullNotes_IsValid()
    {
        ValidationResult result = _sut.Validate(
            new UploadDesignRevisionCommand(new UploadDesignRevisionRequest(Guid.NewGuid(), "https://r2.example.com/v1.png", null)));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Notes");
    }

    [Fact]
    public void Validate_NotesExceedsMaxLength_FailsOnNotes()
    {
        _sut.ShouldFailOn(
            new UploadDesignRevisionCommand(new UploadDesignRevisionRequest(Guid.NewGuid(), "https://r2.example.com/v1.png", new('x', 2001))),
            "Request.Notes");
    }

    [Fact]
    public void Validate_NotesAtMaxLength_IsValid()
    {
        ValidationResult result = _sut.Validate(
            new UploadDesignRevisionCommand(new UploadDesignRevisionRequest(Guid.NewGuid(), "https://r2.example.com/v1.png", new('x', 2000))));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Notes");
    }
}
