using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Clients.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class AddTattooRecordValidatorTests
{
    private readonly AddTattooRecordValidator _sut = new();

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
}
