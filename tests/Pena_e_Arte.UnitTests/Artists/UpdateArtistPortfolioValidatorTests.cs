using Pena_e_Arte.Application.Artists.Commands;
using Pena_e_Arte.Application.Artists.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Artists;

public class UpdateArtistPortfolioValidatorTests
{
    private readonly UpdateArtistPortfolioValidator _sut = new();

    [Fact]
    public void Validate_ValidImagesWithKnownStyles_IsValid()
    {
        _sut.ShouldBeValid(Command([
            new PortfolioImageInput("https://img/1.jpg", "realism"),
            new PortfolioImageInput("https://img/2.jpg", null),
        ]));
    }

    [Fact]
    public void Validate_UnknownStyle_FailsOnStyle()
    {
        _sut.ShouldFailOn(
            Command([new PortfolioImageInput("https://img/1.jpg", "not-a-real-style")]),
            "Request.Images[0].Style");
    }

    [Fact]
    public void Validate_RelativeUrl_FailsOnImageUrl()
    {
        _sut.ShouldFailOn(
            Command([new PortfolioImageInput("/not/absolute.jpg", null)]),
            "Request.Images[0].ImageUrl");
    }

    [Fact]
    public void Validate_EmptyUrl_FailsOnImageUrl()
    {
        _sut.ShouldFailOn(
            Command([new PortfolioImageInput("", null)]),
            "Request.Images[0].ImageUrl");
    }

    [Fact]
    public void Validate_MoreThan50Images_FailsOnCount()
    {
        List<PortfolioImageInput> images = Enumerable.Range(1, 51)
            .Select(i => new PortfolioImageInput($"https://img/{i}.jpg", null))
            .ToList();

        _sut.ShouldFailOn(Command(images), "Request.Images.Count");
    }

    private static UpdateArtistPortfolioCommand Command(List<PortfolioImageInput> images) =>
        new(Guid.NewGuid(), new UpdateArtistPortfolioRequest(images));
}
