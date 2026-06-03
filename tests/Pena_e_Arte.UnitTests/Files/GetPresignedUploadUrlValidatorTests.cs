using Pena_e_Arte.Application.Files.Queries;
using Pena_e_Arte.Application.Files.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Files;

public class GetPresignedUploadUrlValidatorTests
{
    private readonly GetPresignedUploadUrlValidator _sut = new();

    private static GetPresignedUploadUrlQuery ValidQuery(
        string objectKey  = "designs/photo.png",
        string contentType = "image/png") =>
        new(new PresignUploadRequest(objectKey, contentType));

    [Fact]
    public void Validate_ValidImageQuery_IsValid()
    {
        _sut.ShouldBeValid(ValidQuery("designs/photo.png", "image/png"));
    }

    [Fact]
    public void Validate_ValidPdfQuery_IsValid()
    {
        _sut.ShouldBeValid(ValidQuery("consent-forms/form.pdf", "application/pdf"));
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    [InlineData("application/pdf")]
    public void Validate_AllowedContentTypes_IsValid(string contentType)
    {
        _sut.ShouldBeValid(ValidQuery("file.bin", contentType));
    }

    [Fact]
    public void Validate_EmptyObjectKey_FailsOnObjectKey()
    {
        _sut.ShouldFailOn(ValidQuery(""), "Request.ObjectKey");
    }

    [Fact]
    public void Validate_ObjectKeyExceedsMaxLength_FailsOnObjectKey()
    {
        _sut.ShouldFailOn(ValidQuery(new('x', 501)), "Request.ObjectKey");
    }

    [Fact]
    public void Validate_ObjectKeyContainsPathTraversal_FailsOnObjectKey()
    {
        _sut.ShouldFailOn(ValidQuery("../../../etc/passwd"), "Request.ObjectKey");
    }

    [Fact]
    public void Validate_EmptyContentType_FailsOnContentType()
    {
        _sut.ShouldFailOn(ValidQuery(contentType: ""), "Request.ContentType");
    }

    [Fact]
    public void Validate_DisallowedContentType_FailsOnContentType()
    {
        _sut.ShouldFailOn(ValidQuery(contentType: "application/octet-stream"), "Request.ContentType");
    }

    [Fact]
    public void Validate_ScriptContentType_FailsOnContentType()
    {
        _sut.ShouldFailOn(ValidQuery(contentType: "text/javascript"), "Request.ContentType");
    }
}
