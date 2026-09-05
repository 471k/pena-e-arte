using FluentValidation.Results;
using Pena_e_Arte.Application.Public.Queries;
using Pena_e_Arte.Application.Public.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;
using FluentAssertions;

namespace Pena_e_Arte.UnitTests.Public;

public class GetPresignedGuestUploadUrlValidatorTests
{
    private readonly GetPresignedGuestUploadUrlValidator _sut = new();

    [Fact]
    public void Validate_ValidRequest_IsValid()
    {
        _sut.ShouldBeValid(new GetPresignedGuestUploadUrlQuery("guest-studio", new PresignGuestUploadRequest("image/png", "area")));
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("image/gif")]
    [InlineData("text/plain")]
    public void Validate_DisallowedContentType_FailsOnContentType(string contentType)
    {
        _sut.ShouldFailOn(
            new GetPresignedGuestUploadUrlQuery("guest-studio", new PresignGuestUploadRequest(contentType, "area")),
            "Request.ContentType");
    }

    [Theory]
    [InlineData("AreaPhoto")]  // the PascalCase enum name, not the lowercase wire value the validator expects
    [InlineData("body")]
    [InlineData("")]
    public void Validate_DisallowedCategory_FailsOnCategory(string category)
    {
        _sut.ShouldFailOn(
            new GetPresignedGuestUploadUrlQuery("guest-studio", new PresignGuestUploadRequest("image/png", category)),
            "Request.Category");
    }

    [Fact]
    public void Validate_EmptyStudioSlug_FailsOnStudioSlug()
    {
        _sut.ShouldFailOn(
            new GetPresignedGuestUploadUrlQuery("", new PresignGuestUploadRequest("image/png", "area")),
            "StudioSlug");
    }
}
