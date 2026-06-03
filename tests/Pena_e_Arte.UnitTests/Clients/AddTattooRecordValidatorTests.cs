using NSubstitute;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Clients.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class AddTattooRecordValidatorTests
{
    private const string ValidUrl = "https://cdn.example.com/photo.jpg";

    private readonly IR2Service              _r2  = Substitute.For<IR2Service>();
    private readonly AddTattooRecordValidator _sut;

    public AddTattooRecordValidatorTests()
    {
        _r2.IsR2Url(ValidUrl).Returns(true);
        _sut = new AddTattooRecordValidator(_r2);
    }

    private static AddTattooRecordCommand ValidCommand() =>
        Command(Guid.NewGuid(), Guid.NewGuid(), "Dragon sleeve", "left_arm", [], DateTime.UtcNow.AddDays(-1));

    private static AddTattooRecordCommand Command(
        Guid         clientId,
        Guid         artistId,
        string       description,
        string       bodyLocation,
        List<string> photoUrls,
        DateTime     completedAt) =>
        new(clientId, new AddTattooRecordRequest(artistId, null, description, bodyLocation, photoUrls, completedAt));

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(ValidCommand());
    }

    [Fact]
    public void Validate_ValidCommandWithPhotoUrls_IsValid()
    {
        AddTattooRecordCommand cmd = Command(
            Guid.NewGuid(), Guid.NewGuid(), "desc", "arm", [ValidUrl], DateTime.UtcNow.AddDays(-1));
        _sut.ShouldBeValid(cmd);
    }

    [Fact]
    public void Validate_EmptyClientId_FailsOnClientId()
    {
        AddTattooRecordCommand cmd = Command(Guid.Empty, Guid.NewGuid(), "desc", "arm", [], DateTime.UtcNow.AddDays(-1));
        _sut.ShouldFailOn(cmd, "ClientId");
    }

    [Fact]
    public void Validate_EmptyArtistId_FailsOnArtistId()
    {
        AddTattooRecordCommand cmd = Command(Guid.NewGuid(), Guid.Empty, "desc", "arm", [], DateTime.UtcNow.AddDays(-1));
        _sut.ShouldFailOn(cmd, "Request.ArtistId");
    }

    [Fact]
    public void Validate_EmptyDescription_FailsOnDescription()
    {
        _sut.ShouldFailOn(
            Command(Guid.NewGuid(), Guid.NewGuid(), "", "arm", [], DateTime.UtcNow.AddDays(-1)),
            "Request.Description");
    }

    [Fact]
    public void Validate_DescriptionExceedsMaxLength_FailsOnDescription()
    {
        _sut.ShouldFailOn(
            Command(Guid.NewGuid(), Guid.NewGuid(), new('x', 2001), "arm", [], DateTime.UtcNow.AddDays(-1)),
            "Request.Description");
    }

    [Fact]
    public void Validate_EmptyBodyLocation_FailsOnBodyLocation()
    {
        _sut.ShouldFailOn(
            Command(Guid.NewGuid(), Guid.NewGuid(), "desc", "", [], DateTime.UtcNow.AddDays(-1)),
            "Request.BodyLocation");
    }

    [Fact]
    public void Validate_FutureCompletedAt_FailsOnCompletedAt()
    {
        _sut.ShouldFailOn(
            Command(Guid.NewGuid(), Guid.NewGuid(), "desc", "arm", [], DateTime.UtcNow.AddDays(1)),
            "Request.CompletedAt");
    }

    [Fact]
    public void Validate_PhotoUrlExceedsMaxLength_FailsOnPhotoUrls()
    {
        _sut.ShouldFailOn(
            Command(Guid.NewGuid(), Guid.NewGuid(), "desc", "arm", [new('x', 2049)], DateTime.UtcNow.AddDays(-1)),
            "Request.PhotoUrls[0]");
    }

    [Fact]
    public void Validate_PhotoUrlNotFromR2_FailsOnPhotoUrls()
    {
        const string externalUrl = "https://external.attacker.com/photo.jpg";
        _r2.IsR2Url(externalUrl).Returns(false);

        _sut.ShouldFailOn(
            Command(Guid.NewGuid(), Guid.NewGuid(), "desc", "arm", [externalUrl], DateTime.UtcNow.AddDays(-1)),
            "Request.PhotoUrls[0]");
    }
}
