using FluentAssertions;
using Pena_e_Arte.Application.Reports.Queries;
using Pena_e_Arte.Application.Reports.Validators;

namespace Pena_e_Arte.UnitTests.Reports;

public class ExportRevenueCsvValidatorTests
{
    private readonly ExportRevenueCsvValidator _sut = new();

    [Fact]
    public void Validate_NoRange_IsValid()
    {
        _sut.Validate(new ExportRevenueCsvQuery(null, null)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ToOnOrAfterFrom_IsValid()
    {
        DateTime from = DateTime.UtcNow.AddDays(-10);
        DateTime to = DateTime.UtcNow;
        _sut.Validate(new ExportRevenueCsvQuery(from, to)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ToBeforeFrom_IsInvalid()
    {
        DateTime from = DateTime.UtcNow;
        DateTime to = DateTime.UtcNow.AddDays(-10);
        _sut.Validate(new ExportRevenueCsvQuery(from, to)).IsValid.Should().BeFalse();
    }
}
