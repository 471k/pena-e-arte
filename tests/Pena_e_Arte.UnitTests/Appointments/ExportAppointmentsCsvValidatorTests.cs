using FluentAssertions;
using Pena_e_Arte.Application.Appointments.Queries;
using Pena_e_Arte.Application.Appointments.Validators;

namespace Pena_e_Arte.UnitTests.Appointments;

public class ExportAppointmentsCsvValidatorTests
{
    private readonly ExportAppointmentsCsvValidator _sut = new();

    [Fact]
    public void Validate_NoRange_IsValid()
    {
        _sut.Validate(new ExportAppointmentsCsvQuery(null, null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ToOnOrAfterFrom_IsValid()
    {
        DateTime from = DateTime.UtcNow.AddDays(-10);
        DateTime to = DateTime.UtcNow;
        _sut.Validate(new ExportAppointmentsCsvQuery(from, to)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ToBeforeFrom_IsInvalid()
    {
        DateTime from = DateTime.UtcNow;
        DateTime to = DateTime.UtcNow.AddDays(-10);
        _sut.Validate(new ExportAppointmentsCsvQuery(from, to)).IsValid.Should().BeFalse();
    }
}
