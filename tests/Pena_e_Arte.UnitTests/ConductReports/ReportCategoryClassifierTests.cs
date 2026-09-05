using FluentAssertions;
using Pena_e_Arte.Domain.Constants;
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.UnitTests.ConductReports;

public class ReportCategoryClassifierTests
{
    [Theory]
    [InlineData(ReportCategory.Scam, true)]
    [InlineData(ReportCategory.SexualMisconduct, true)]
    [InlineData(ReportCategory.UnsafeHygienePractices, true)]
    [InlineData(ReportCategory.Harassment, true)]
    [InlineData(ReportCategory.Discrimination, true)]
    [InlineData(ReportCategory.PoorServiceQuality, false)]
    [InlineData(ReportCategory.Other, false)]
    public void IsHighSeverity_ReturnsExpectedClassification(ReportCategory category, bool expected)
    {
        ReportCategoryClassifier.IsHighSeverity(category).Should().Be(expected);
    }

    [Fact]
    public void IsHighSeverity_EveryEnumValueHasBeenExplicitlyClassified()
    {
        // Data-driven Theory above would silently default a newly-added, un-classified
        // category to "Standard" (HashSet.Contains returns false for anything not listed).
        // This assertion catches that: every value in the current enum must appear in one of
        // the two InlineData groups above, so a future category with no classification
        // decision fails a test instead of shipping unnoticed.
        ReportCategory[] allCategories = Enum.GetValues<ReportCategory>();
        ReportCategory[] expectedHigh =
        [
            ReportCategory.Scam,
            ReportCategory.SexualMisconduct,
            ReportCategory.UnsafeHygienePractices,
            ReportCategory.Harassment,
            ReportCategory.Discrimination,
        ];
        ReportCategory[] expectedStandard = [ReportCategory.PoorServiceQuality, ReportCategory.Other];

        (expectedHigh.Length + expectedStandard.Length).Should().Be(allCategories.Length,
            "every ReportCategory value must be accounted for in exactly one severity bucket");
        allCategories.Should().BeEquivalentTo(expectedHigh.Concat(expectedStandard));
    }
}
