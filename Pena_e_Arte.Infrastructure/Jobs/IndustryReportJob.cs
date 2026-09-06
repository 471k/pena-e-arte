using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Infrastructure.Jobs;

public class IndustryReportJob(AppDbContext db, IR2Service r2)
{
    private const string ReportPrefix = "reports/industry/";
    private const int MinCohortSize = 10;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public async Task RunAsync(CancellationToken ct = default)
    {
        // Approved exception #4: industry report aggregate — admin-level, no PII
        if (db.Database.IsRelational())
            db.Database.SetCommandTimeout(300);

        DateTime now = DateTime.UtcNow;
        DateTime cutoff90 = now.AddDays(-90);

        // total_active_studios
        int totalActive = await db.Studios
            .IgnoreQueryFilters()
            .CountAsync(s => s.Subscription != null &&
                             s.Subscription.Status == SubscriptionStatus.Active, ct);

        // Load recent appointments once for multiple metrics
        var recentAppointments = await db.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.Date >= cutoff90 && a.DeletedAt == null)
            .Select(a => new { a.StudioId, a.Date, a.DurationMinutes })
            .ToListAsync(ct);

        // avg_appointments_per_studio_per_month
        double avgAppointmentsPerMonth = 0;
        if (recentAppointments.Count > 0)
        {
            double totalInPeriod = recentAppointments
                .GroupBy(a => a.StudioId)
                .Average(g => g.Count());
            avgAppointmentsPerMonth = totalInPeriod / 3.0; // 90 days ≈ 3 months
        }

        // peak_booking_hour
        int? peakBookingHour = recentAppointments.Count > 0
            ? recentAppointments
                .GroupBy(a => a.Date.Hour)
                .OrderByDescending(g => g.Count())
                .Select(g => (int?)g.Key)
                .FirstOrDefault()
            : null;

        // top_session_durations_minutes
        int[] durationBuckets = [30, 60, 90, 120, 180];
        Dictionary<string, int> durationCounts = durationBuckets.ToDictionary(
            b => b.ToString(),
            b => recentAppointments.Count(a => a.DurationMinutes == b));

        // trial_to_paid_conversion_rate
        int trialsStarted = await db.Subscriptions
            .IgnoreQueryFilters()
            .CountAsync(s => s.CreatedAt >= cutoff90, ct);

        int trialsConverted = await db.Subscriptions
            .IgnoreQueryFilters()
            .CountAsync(s => s.CreatedAt >= cutoff90 && s.Status == SubscriptionStatus.Active, ct);

        double conversionRate = trialsStarted > 0
            ? (double)trialsConverted / trialsStarted
            : 0;

        // avg_retention_months (first to last appointment per studio, all time)
        var allAppointmentDates = await db.Appointments
            .IgnoreQueryFilters()
            .Where(a => a.DeletedAt == null)
            .Select(a => new { a.StudioId, a.Date })
            .ToListAsync(ct);

        double avgRetentionMonths = 0;
        if (allAppointmentDates.Count > 0)
        {
            avgRetentionMonths = allAppointmentDates
                .GroupBy(a => a.StudioId)
                .Average(g => (g.Max(a => a.Date) - g.Min(a => a.Date)).TotalDays / 30.0);
        }

        IndustryAggregates aggregates = new(
            TotalActiveStudios: totalActive,
            AvgAppointmentsPerStudioPerMonth: avgAppointmentsPerMonth,
            PeakBookingHour: peakBookingHour,
            TopSessionDurations: durationCounts,
            TrialToPaidConversionRate: conversionRate,
            AvgRetentionMonths: avgRetentionMonths);

        IndustryReportDocument doc = BuildDocument(aggregates, now);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(doc, SerializerOptions);

        string key = $"{ReportPrefix}{now.Year}-{now.Month:D2}.json";
        await r2.UploadAsync(key, json, "application/json", ct);
    }

    internal static IndustryReportDocument BuildDocument(IndustryAggregates data, DateTime generatedAt)
    {
        bool sufficient = data.TotalActiveStudios >= MinCohortSize;

        return new IndustryReportDocument(
            GeneratedAt: generatedAt.ToString("O"),
            Period: $"{generatedAt.Year}-{generatedAt.Month:D2}",
            CohortSize: data.TotalActiveStudios,
            Metrics: new IndustryMetrics(
                AvgAppointmentsPerStudioPerMonth: sufficient ? data.AvgAppointmentsPerStudioPerMonth : null,
                PeakBookingHour: sufficient ? data.PeakBookingHour : null,
                TopSessionDurationsMinutes: sufficient ? data.TopSessionDurations : null,
                TrialToPaidConversionRate: sufficient ? data.TrialToPaidConversionRate : null,
                AvgRetentionMonths: sufficient ? data.AvgRetentionMonths : null),
            Note: "Metrics suppressed where cohort < 10.");
    }
}

internal record IndustryAggregates(
    int TotalActiveStudios,
    double AvgAppointmentsPerStudioPerMonth,
    int? PeakBookingHour,
    Dictionary<string, int> TopSessionDurations,
    double TrialToPaidConversionRate,
    double AvgRetentionMonths
);

internal record IndustryReportDocument(
    [property: JsonPropertyName("generated_at")] string GeneratedAt,
    [property: JsonPropertyName("period")] string Period,
    [property: JsonPropertyName("cohort_size")] int CohortSize,
    [property: JsonPropertyName("metrics")] IndustryMetrics Metrics,
    [property: JsonPropertyName("note")] string Note
);

internal record IndustryMetrics(
    [property: JsonPropertyName("avg_appointments_per_studio_per_month")] double? AvgAppointmentsPerStudioPerMonth,
    [property: JsonPropertyName("peak_booking_hour_utc")] int? PeakBookingHour,
    [property: JsonPropertyName("top_session_durations_minutes")] Dictionary<string, int>? TopSessionDurationsMinutes,
    [property: JsonPropertyName("trial_to_paid_conversion_rate")] double? TrialToPaidConversionRate,
    [property: JsonPropertyName("avg_retention_months")] double? AvgRetentionMonths
);
