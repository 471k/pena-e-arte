using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Application.Appointments.Validators;
using Pena_e_Arte.UnitTests.Helpers;

namespace Pena_e_Arte.UnitTests.Appointments;

public class CheckSlotAvailabilityValidatorTests
{
    private readonly CheckSlotAvailabilityValidator _validator = new();

    private static CheckSlotAvailabilityQuery Valid() => new(
        Guid.NewGuid(), DateTime.UtcNow.AddDays(1), 60);

    [Fact]
    public void Validate_ValidQuery_Passes()
    {
        _validator.ShouldBeValid(Valid());
    }

    [Fact]
    public void Validate_NullArtistId_Passes()
    {
        // Studio-choice slot check — no specific artist — is a legitimately valid query.
        _validator.ShouldBeValid(Valid() with { ArtistId = null });
    }

    [Fact]
    public void Validate_PastDate_FailsOnDate()
    {
        _validator.ShouldFailOn(Valid() with { Date = DateTime.UtcNow.AddDays(-1) }, "Date");
    }

    [Fact]
    public void Validate_DurationBelowMinimum_FailsOnDurationMinutes()
    {
        _validator.ShouldFailOn(Valid() with { DurationMinutes = 29 }, "DurationMinutes");
    }

    [Fact]
    public void Validate_DurationAboveMaximum_FailsOnDurationMinutes()
    {
        _validator.ShouldFailOn(Valid() with { DurationMinutes = 481 }, "DurationMinutes");
    }
}
