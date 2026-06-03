using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Clients.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class UpdateTattooRecordValidatorTests
{
    private readonly UpdateTattooRecordValidator _sut = new();

    private static UpdateTattooRecordCommand ValidCommand() =>
        new(Guid.NewGuid(), Guid.NewGuid(),
            new UpdateTattooRecordRequest("Dragon sleeve", "left_arm", [], DateTime.UtcNow.AddDays(-1)));

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(ValidCommand());
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
}
