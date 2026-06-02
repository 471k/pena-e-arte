using FluentAssertions;
using FluentValidation.Results;
using Pena_e_Arte.Application.Designs.Commands;
using Pena_e_Arte.Application.Designs.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Designs;

public class CreateDesignValidatorTests
{
    private readonly CreateDesignValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(ValidCommand());
    }

    [Fact]
    public void Validate_EmptyClientId_FailsOnClientId()
    {
        _sut.ShouldFailOn(Command(Guid.Empty, Guid.NewGuid(), "Rose", null), "Request.ClientId");
    }

    [Fact]
    public void Validate_EmptyArtistId_FailsOnArtistId()
    {
        _sut.ShouldFailOn(Command(Guid.NewGuid(), Guid.Empty, "Rose", null), "Request.ArtistId");
    }

    [Fact]
    public void Validate_EmptyTitle_FailsOnTitle()
    {
        _sut.ShouldFailOn(Command(Guid.NewGuid(), Guid.NewGuid(), "", null), "Request.Title");
    }

    [Fact]
    public void Validate_TitleExceedsMaxLength_FailsOnTitle()
    {
        _sut.ShouldFailOn(Command(Guid.NewGuid(), Guid.NewGuid(), new('x', 201), null), "Request.Title");
    }

    [Fact]
    public void Validate_NullDescription_IsValid()
    {
        ValidationResult result = _sut.Validate(Command(Guid.NewGuid(), Guid.NewGuid(), "Rose", null));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Description");
    }

    [Fact]
    public void Validate_DescriptionExceedsMaxLength_FailsOnDescription()
    {
        _sut.ShouldFailOn(Command(Guid.NewGuid(), Guid.NewGuid(), "Rose", new('x', 2001)), "Request.Description");
    }

    [Fact]
    public void Validate_DescriptionAtMaxLength_IsValid()
    {
        ValidationResult result = _sut.Validate(Command(Guid.NewGuid(), Guid.NewGuid(), "Rose", new('x', 2000)));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Description");
    }

    private static CreateDesignCommand ValidCommand() =>
        Command(Guid.NewGuid(), Guid.NewGuid(), "Rose tattoo", "A small rose");

    private static CreateDesignCommand Command(Guid clientId, Guid artistId, string title, string? desc) =>
        new(new CreateDesignRequest(clientId, artistId, title, desc));
}
