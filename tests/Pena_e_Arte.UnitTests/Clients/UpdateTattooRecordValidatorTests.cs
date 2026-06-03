using NSubstitute;
using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Clients.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class UpdateTattooRecordValidatorTests
{
    private const string ValidUrl = "https://cdn.example.com/photo.jpg";

    private readonly IR2Service                 _r2  = Substitute.For<IR2Service>();
    private readonly UpdateTattooRecordValidator _sut;

    public UpdateTattooRecordValidatorTests()
    {
        _r2.IsR2Url(ValidUrl).Returns(true);
        _sut = new UpdateTattooRecordValidator(_r2);
    }

    private static UpdateTattooRecordCommand ValidCommand() =>
        new(Guid.NewGuid(), Guid.NewGuid(),
            new UpdateTattooRecordRequest("Dragon sleeve", "left_arm", [], DateTime.UtcNow.AddDays(-1)));

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(ValidCommand());
    }

    [Fact]
    public void Validate_ValidCommandWithPhotoUrls_IsValid()
    {
        UpdateTattooRecordCommand cmd = new(Guid.NewGuid(), Guid.NewGuid(),
            new UpdateTattooRecordRequest("desc", "arm", [ValidUrl], DateTime.UtcNow.AddDays(-1)));
        _sut.ShouldBeValid(cmd);
    }

    [Fact]
    public void Validate_EmptyClientId_FailsOnClientId()
    {
        UpdateTattooRecordCommand cmd = new(Guid.Empty, Guid.NewGuid(),
            new("desc", "arm", [], DateTime.UtcNow.AddDays(-1)));
        _sut.ShouldFailOn(cmd, "ClientId");
    }

    [Fact]
    public void Validate_EmptyId_FailsOnId()
    {
        UpdateTattooRecordCommand cmd = new(Guid.NewGuid(), Guid.Empty,
            new("desc", "arm", [], DateTime.UtcNow.AddDays(-1)));
        _sut.ShouldFailOn(cmd, "Id");
    }

    [Fact]
    public void Validate_EmptyDescription_FailsOnDescription()
    {
        UpdateTattooRecordCommand cmd = new(Guid.NewGuid(), Guid.NewGuid(),
            new("", "arm", [], DateTime.UtcNow.AddDays(-1)));
        _sut.ShouldFailOn(cmd, "Request.Description");
    }

    [Fact]
    public void Validate_DescriptionExceedsMaxLength_FailsOnDescription()
    {
        UpdateTattooRecordCommand cmd = new(Guid.NewGuid(), Guid.NewGuid(),
            new(new string('x', 2001), "arm", [], DateTime.UtcNow.AddDays(-1)));
        _sut.ShouldFailOn(cmd, "Request.Description");
    }

    [Fact]
    public void Validate_EmptyBodyLocation_FailsOnBodyLocation()
    {
        UpdateTattooRecordCommand cmd = new(Guid.NewGuid(), Guid.NewGuid(),
            new("desc", "", [], DateTime.UtcNow.AddDays(-1)));
        _sut.ShouldFailOn(cmd, "Request.BodyLocation");
    }

    [Fact]
    public void Validate_FutureCompletedAt_FailsOnCompletedAt()
    {
        UpdateTattooRecordCommand cmd = new(Guid.NewGuid(), Guid.NewGuid(),
            new("desc", "arm", [], DateTime.UtcNow.AddDays(1)));
        _sut.ShouldFailOn(cmd, "Request.CompletedAt");
    }

    [Fact]
    public void Validate_PhotoUrlExceedsMaxLength_FailsOnPhotoUrls()
    {
        UpdateTattooRecordCommand cmd = new(Guid.NewGuid(), Guid.NewGuid(),
            new("desc", "arm", [new string('x', 2049)], DateTime.UtcNow.AddDays(-1)));
        _sut.ShouldFailOn(cmd, "Request.PhotoUrls[0]");
    }

    [Fact]
    public void Validate_PhotoUrlNotFromR2_FailsOnPhotoUrls()
    {
        const string externalUrl = "https://external.attacker.com/photo.jpg";
        _r2.IsR2Url(externalUrl).Returns(false);

        UpdateTattooRecordCommand cmd = new(Guid.NewGuid(), Guid.NewGuid(),
            new("desc", "arm", [externalUrl], DateTime.UtcNow.AddDays(-1)));
        _sut.ShouldFailOn(cmd, "Request.PhotoUrls[0]");
    }
}
