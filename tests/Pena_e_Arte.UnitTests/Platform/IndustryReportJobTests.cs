using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Pena_e_Arte.Infrastructure.Jobs;

namespace Pena_e_Arte.UnitTests.Platform;

public class IndustryReportJobTests
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static IndustryAggregates FullAggregates(int cohort = 42) => new(
        TotalActiveStudios:              cohort,
        AvgAppointmentsPerStudioPerMonth: 12.5,
        PeakBookingHour:                 14,
        TopSessionDurations:             new() { ["30"] = 3, ["60"] = 20, ["90"] = 10, ["120"] = 5, ["180"] = 2 },
        TrialToPaidConversionRate:       0.65,
        AvgRetentionMonths:              8.2);

    // ── Cohort suppression ────────────────────────────────────────────────────────

    [Fact]
    public void BuildDocument_CohortBelowMinimum_AllMetricsNull()
    {
        IndustryReportDocument doc = IndustryReportJob.BuildDocument(
            FullAggregates(cohort: 5), DateTime.UtcNow);

        doc.Metrics.AvgAppointmentsPerStudioPerMonth.Should().BeNull();
        doc.Metrics.PeakBookingHour.Should().BeNull();
        doc.Metrics.TopSessionDurationsMinutes.Should().BeNull();
        doc.Metrics.TrialToPaidConversionRate.Should().BeNull();
        doc.Metrics.AvgRetentionMonths.Should().BeNull();
    }

    [Fact]
    public void BuildDocument_CohortBelowMinimum_CohortSizeStillPresent()
    {
        IndustryReportDocument doc = IndustryReportJob.BuildDocument(
            FullAggregates(cohort: 3), DateTime.UtcNow);

        doc.CohortSize.Should().Be(3);
    }

    [Fact]
    public void BuildDocument_CohortAtMinimum_MetricsPresent()
    {
        IndustryReportDocument doc = IndustryReportJob.BuildDocument(
            FullAggregates(cohort: 10), DateTime.UtcNow);

        doc.Metrics.AvgAppointmentsPerStudioPerMonth.Should().NotBeNull();
        doc.Metrics.PeakBookingHour.Should().NotBeNull();
        doc.Metrics.TopSessionDurationsMinutes.Should().NotBeNull();
        doc.Metrics.TrialToPaidConversionRate.Should().NotBeNull();
        doc.Metrics.AvgRetentionMonths.Should().NotBeNull();
    }

    [Fact]
    public void BuildDocument_CohortAboveMinimum_MetricsMatchInput()
    {
        IndustryAggregates aggregates = FullAggregates(cohort: 42);

        IndustryReportDocument doc = IndustryReportJob.BuildDocument(aggregates, DateTime.UtcNow);

        doc.Metrics.AvgAppointmentsPerStudioPerMonth.Should().BeApproximately(12.5, 0.001);
        doc.Metrics.PeakBookingHour.Should().Be(14);
        doc.Metrics.TrialToPaidConversionRate.Should().BeApproximately(0.65, 0.001);
        doc.Metrics.AvgRetentionMonths.Should().BeApproximately(8.2, 0.001);
    }

    // ── No identifiers in output ──────────────────────────────────────────────────

    [Fact]
    public void BuildDocument_SerializedOutput_ContainsNoIdentifyingFields()
    {
        IndustryReportDocument doc  = IndustryReportJob.BuildDocument(FullAggregates(), DateTime.UtcNow);
        string                 json = JsonSerializer.Serialize(doc, new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        });

        // Must not contain any identifying property names
        json.Should().NotContain("studioId",  because: "output must be identifier-free");
        json.Should().NotContain("tenantId",  because: "output must be identifier-free");
        json.Should().NotContain("userId",    because: "output must be identifier-free");
        json.Should().NotContain("email",     because: "output must be identifier-free");
        json.Should().NotContain("artistId",  because: "output must be identifier-free");
        json.Should().NotContain("clientId",  because: "output must be identifier-free");
        json.Should().NotContain("studio_id", because: "output must be identifier-free");
        json.Should().NotContain("user_id",   because: "output must be identifier-free");
    }

    // ── Document structure ────────────────────────────────────────────────────────

    [Fact]
    public void BuildDocument_PeriodFormat_MatchesYearMonth()
    {
        DateTime date = new(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

        IndustryReportDocument doc = IndustryReportJob.BuildDocument(FullAggregates(), date);

        doc.Period.Should().Be("2026-06");
    }

    [Fact]
    public void BuildDocument_GeneratedAt_IsIso8601()
    {
        DateTime date = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

        IndustryReportDocument doc = IndustryReportJob.BuildDocument(FullAggregates(), date);

        DateTime.TryParse(doc.GeneratedAt, out _).Should().BeTrue();
    }
}
