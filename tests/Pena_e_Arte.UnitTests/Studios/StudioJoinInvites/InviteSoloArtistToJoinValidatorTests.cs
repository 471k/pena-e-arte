using Pena_e_Arte.Application.Studios.StudioJoinInvites;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Studios.StudioJoinInvites;

public class InviteSoloArtistToJoinValidatorTests
{
    private readonly InviteSoloArtistToJoinValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(Command("Jane", "Doe", "jane@example.com", null, null));
    }

    [Fact]
    public void Validate_EmptyEmail_FailsOnEmail()
    {
        _sut.ShouldFailOn(Command("Jane", "Doe", "", null, null), "Request.Email");
    }

    [Fact]
    public void Validate_InvalidEmailFormat_FailsOnEmail()
    {
        _sut.ShouldFailOn(Command("Jane", "Doe", "not-an-email", null, null), "Request.Email");
    }

    [Fact]
    public void Validate_EmptyFirstName_FailsOnFirstName()
    {
        _sut.ShouldFailOn(Command("", "Doe", "jane@example.com", null, null), "Request.FirstName");
    }

    [Fact]
    public void Validate_EmptyLastName_FailsOnLastName()
    {
        _sut.ShouldFailOn(Command("Jane", "", "jane@example.com", null, null), "Request.LastName");
    }

    [Fact]
    public void Validate_SpecializationsExceedsMaxLength_FailsOnSpecializations()
    {
        // StudioJoinInvite.Specializations is varchar(1000), matching Artist.Specializations
        // exactly — this value is copied verbatim onto the real Artist at accept time.
        _sut.ShouldFailOn(
            Command("Jane", "Doe", "jane@example.com", new('x', 1001), null), "Request.Specializations");
    }

    [Fact]
    public void Validate_SpecializationsAtMaxLength_IsValid()
    {
        _sut.ShouldBeValid(Command("Jane", "Doe", "jane@example.com", new('x', 1000), null));
    }

    [Fact]
    public void Validate_HourlyRateZero_FailsOnHourlyRate()
    {
        _sut.ShouldFailOn(Command("Jane", "Doe", "jane@example.com", null, 0m), "Request.HourlyRate");
    }

    [Fact]
    public void Validate_HourlyRateExceedsMax_FailsOnHourlyRate()
    {
        _sut.ShouldFailOn(Command("Jane", "Doe", "jane@example.com", null, 10_000.01m), "Request.HourlyRate");
    }

    [Fact]
    public void Validate_HourlyRateWithinRange_IsValid()
    {
        _sut.ShouldBeValid(Command("Jane", "Doe", "jane@example.com", null, 90m));
    }

    [Fact]
    public void Validate_NullHourlyRate_IsValid()
    {
        _sut.ShouldBeValid(Command("Jane", "Doe", "jane@example.com", null, null));
    }

    private static InviteSoloArtistToJoinCommand Command(
        string first, string last, string email, string? specializations, decimal? hourlyRate) =>
        new(new CreateArtistRequest(first, last, email, specializations, hourlyRate));
}
