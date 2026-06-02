using Pena_e_Arte.Application.Clients.Commands;
using Pena_e_Arte.Application.Clients.Validators;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Clients;

public class UpsertClientProfileValidatorTests
{
    private readonly UpsertClientProfileValidator _sut = new();

    [Fact]
    public void Validate_ValidCommand_IsValid()
    {
        _sut.ShouldBeValid(Command(new DateOnly(1990, 1, 1), "None", "Latex"));
    }

    [Fact]
    public void Validate_AllNullOptionalFields_IsValid()
    {
        _sut.ShouldBeValid(Command(null, null, null));
    }

    [Fact]
    public void Validate_EmptyClientId_FailsOnClientId()
    {
        UpsertClientProfileCommand cmd = new(Guid.Empty, new UpsertClientProfileRequest(null, null, null));
        _sut.ShouldFailOn(cmd, "ClientId");
    }

    [Fact]
    public void Validate_MedicalNotesExceedsMaxLength_FailsOnMedicalNotes()
    {
        _sut.ShouldFailOn(Command(null, new('x', 4001), null), "Request.MedicalNotes");
    }

    [Fact]
    public void Validate_AllergiesExceedsMaxLength_FailsOnAllergies()
    {
        _sut.ShouldFailOn(Command(null, null, new('x', 1001)), "Request.Allergies");
    }

    [Fact]
    public void Validate_FutureDateOfBirth_FailsOnDateOfBirth()
    {
        _sut.ShouldFailOn(Command(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)), null, null), "Request.DateOfBirth");
    }

    private static UpsertClientProfileCommand Command(DateOnly? dob, string? notes, string? allergies) =>
        new(Guid.NewGuid(), new UpsertClientProfileRequest(dob, notes, allergies));
}
