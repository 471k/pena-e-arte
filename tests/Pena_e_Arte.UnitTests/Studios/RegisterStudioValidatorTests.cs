using FluentAssertions;
using FluentValidation.Results;
using Pena_e_Arte.Application.Studios.Commands;
using Pena_e_Arte.Application.Studios.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios;

public class RegisterStudioValidatorTests
{
    private readonly RegisterStudioValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(ValidCommand());
    }

    [Fact]
    public void Validate_EmptyName_FailsOnName()
    {
        _sut.ShouldFailOn(Command("", "my-studio", "Lisbon", 38.7, -9.1), "Request.Name");
    }

    [Fact]
    public void Validate_NameExceedsMaxLength_FailsOnName()
    {
        _sut.ShouldFailOn(Command(new('x', 201), "my-studio", "Lisbon", 38.7, -9.1), "Request.Name");
    }

    [Fact]
    public void Validate_EmptySlug_FailsOnSlug()
    {
        _sut.ShouldFailOn(Command("Studio", "", "Lisbon", 38.7, -9.1), "Request.Slug");
    }

    [Fact]
    public void Validate_SlugExceedsMaxLength_FailsOnSlug()
    {
        _sut.ShouldFailOn(Command("Studio", new('a', 101), "Lisbon", 38.7, -9.1), "Request.Slug");
    }

    [Theory]
    [InlineData("My Studio")]
    [InlineData("UPPERCASE")]
    [InlineData("slug_underscores")]
    [InlineData("slug@special")]
    public void Validate_SlugWithInvalidCharacters_FailsOnSlug(string slug)
    {
        _sut.ShouldFailOn(Command("Studio", slug, "Lisbon", 38.7, -9.1), "Request.Slug");
    }

    [Theory]
    [InlineData("valid-slug")]
    [InlineData("slug123")]
    [InlineData("my-studio-2")]
    public void Validate_SlugWithValidCharacters_DoesNotFailOnSlug(string slug)
    {
        ValidationResult result = _sut.Validate(Command("Studio", slug, "Lisbon", 38.7, -9.1));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Slug");
    }

    [Fact]
    public void Validate_EmptyCity_FailsOnCity()
    {
        _sut.ShouldFailOn(Command("Studio", "my-studio", "", 38.7, -9.1), "Request.City");
    }

    [Fact]
    public void Validate_CityExceedsMaxLength_FailsOnCity()
    {
        _sut.ShouldFailOn(Command("Studio", "my-studio", new('x', 101), 38.7, -9.1), "Request.City");
    }

    [Theory]
    [InlineData(-91.0)]
    [InlineData(91.0)]
    public void Validate_LatitudeOutOfRange_FailsOnLatitude(double lat)
    {
        _sut.ShouldFailOn(Command("Studio", "my-studio", "Lisbon", lat, -9.1), "Request.Latitude");
    }

    [Theory]
    [InlineData(-90.0)]
    [InlineData(0.0)]
    [InlineData(90.0)]
    public void Validate_LatitudeAtBoundaries_DoesNotFailOnLatitude(double lat)
    {
        ValidationResult result = _sut.Validate(Command("Studio", "my-studio", "Lisbon", lat, -9.1));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Latitude");
    }

    [Theory]
    [InlineData(-181.0)]
    [InlineData(181.0)]
    public void Validate_LongitudeOutOfRange_FailsOnLongitude(double lon)
    {
        _sut.ShouldFailOn(Command("Studio", "my-studio", "Lisbon", 38.7, lon), "Request.Longitude");
    }

    [Theory]
    [InlineData(-180.0)]
    [InlineData(0.0)]
    [InlineData(180.0)]
    public void Validate_LongitudeAtBoundaries_DoesNotFailOnLongitude(double lon)
    {
        ValidationResult result = _sut.Validate(Command("Studio", "my-studio", "Lisbon", 38.7, lon));
        result.Errors.Should().NotContain(e => e.PropertyName == "Request.Longitude");
    }

    [Fact]
    public void Validate_EmptyOwnerEmail_FailsOnOwnerEmail()
    {
        _sut.ShouldFailOn(Command("Studio", "my-studio", "Lisbon", 38.7, -9.1, ""), "Request.OwnerEmail");
    }

    [Fact]
    public void Validate_InvalidOwnerEmail_FailsOnOwnerEmail()
    {
        _sut.ShouldFailOn(Command("Studio", "my-studio", "Lisbon", 38.7, -9.1, "not-an-email"), "Request.OwnerEmail");
    }

    private static RegisterStudioCommand ValidCommand() =>
        Command("Tinta & Alma", "tinta-alma", "Porto", 41.15, -8.61, "owner@tinta-alma.com");

    private static RegisterStudioCommand Command(
        string name, string slug, string city, double lat, double lon,
        string ownerEmail = "owner@example.com") =>
        new(new RegisterStudioRequest(name, slug, city, lat, lon, ownerEmail));
}
