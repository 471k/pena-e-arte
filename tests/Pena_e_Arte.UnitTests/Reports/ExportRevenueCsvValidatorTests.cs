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
        _sut.Validate(new ExportRevenueCsvQuery(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31))).IsValid.Should().BeTrue();
        _sut.Validate(new ExportRevenueCsvQuery(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1))).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ToBeforeFrom_IsInvalid()
    {
        _sut.Validate(new ExportRevenueCsvQuery(new DateOnly(2026, 10, 31), new DateOnly(2026, 10, 1))).IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("Paid")]
    [InlineData("Refunded")]
    public void Validate_AStatusTheExportCanHold_IsValid(string status)
    {
        _sut.Validate(new ExportRevenueCsvQuery(null, null, Status: status)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("Pending")]  // a real status, but never money received, so never in this file
    [InlineData("Failed")]
    [InlineData("paid")]
    [InlineData("1")]
    public void Validate_AnyOtherStatus_IsRefused_RatherThanAnEmptyFile(string status)
    {
        _sut.Validate(new ExportRevenueCsvQuery(null, null, Status: status)).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Validate_SearchLongerThanTheCap_IsInvalid()
    {
        _sut.Validate(new ExportRevenueCsvQuery(null, null, Search: new string('x', 101))).IsValid.Should().BeFalse();
    }
}
