using FluentAssertions;
using FluentValidation.Results;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Application.Designs.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class ReviewDesignValidatorTests
{
    private readonly ReviewDesignValidator _sut = new();

    [Fact]
    public void Validate_ValidApproveCommand_IsValid()
    {
        _sut.ShouldBeValid(new ReviewDesignCommand(new ReviewDesignRequest(Guid.NewGuid(), true, null)));
    }

    [Fact]
    public void Validate_ValidRequestChangesCommand_IsValid()
    {
        _sut.ShouldBeValid(new ReviewDesignCommand(new ReviewDesignRequest(Guid.NewGuid(), false, "Fix the linework")));
    }

    [Fact]
    public void Validate_EmptyDesignRevisionId_FailsOnRevisionId()
    {
        _sut.ShouldFailOn(
            new ReviewDesignCommand(new ReviewDesignRequest(Guid.Empty, true, null)),
            "Request.DesignRevisionId");
    }

    [Fact]
    public void Validate_NullNotes_IsValid()
    {
        ValidationResult result = _sut.Validate(new ReviewDesignCommand(new ReviewDesignRequest(Guid.NewGuid(), false, null)));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Notes");
    }

    [Fact]
    public void Validate_NotesExceedsMaxLength_FailsOnNotes()
    {
        _sut.ShouldFailOn(
            new ReviewDesignCommand(new ReviewDesignRequest(Guid.NewGuid(), false, new('x', 2001))),
            "Request.Notes");
    }

    [Fact]
    public void Validate_NotesAtMaxLength_IsValid()
    {
        ValidationResult result = _sut.Validate(new ReviewDesignCommand(new ReviewDesignRequest(Guid.NewGuid(), false, new('x', 2000))));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Notes");
    }
}
