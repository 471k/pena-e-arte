using FluentAssertions;
using NSubstitute;
using Pena_e_Arte.Application.Platform.Queries;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.IntegrationTests.Infrastructure;
using System.Text;
using System.Text.Json;

namespace Pena_e_Arte.IntegrationTests.Application;

[Collection("Database")]
public class IndustryReportsIntegrationTests(DatabaseFixture fixture)
{
    // ── GetIndustryReportsHandler — 200 for issuer ────────────────────────────────
    // Authorization is enforced at the API endpoint level (RequireAuthorization("IssuerOnly")).
    // These tests verify the handler behavior independently of HTTP auth.

    [Fact]
    public async Task GetIndustryReports_EmptyR2_ReturnsEmptyList()
    {
        IR2Service r2 = Substitute.For<IR2Service>();
        r2.ListByPrefixAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<R2ObjectInfo>>(Array.Empty<R2ObjectInfo>()));

        GetIndustryReportsHandler handler = new(r2);
        IReadOnlyList<IndustryReportSummaryResponse> result =
            await handler.Handle(new GetIndustryReportsQuery(), default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetIndustryReports_WithReports_ReturnsSummariesWithDownloadUrls()
    {
        IR2Service r2 = Substitute.For<IR2Service>();
        r2.ListByPrefixAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<R2ObjectInfo>>(new[]
            {
                new R2ObjectInfo("reports/industry/2026-05.json", new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), 1024L),
                new R2ObjectInfo("reports/industry/2026-06.json", new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc), 1024L),
            }));
        r2.GeneratePresignedReadUrlAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(call => $"https://r2.example.com/signed/{call.ArgAt<string>(0)}");

        GetIndustryReportsHandler handler = new(r2);
        IReadOnlyList<IndustryReportSummaryResponse> result =
            await handler.Handle(new GetIndustryReportsQuery(), default);

        result.Should().HaveCount(2);
        // Results are ordered descending by key
        result[0].Period.Should().Be("2026-06");
        result[1].Period.Should().Be("2026-05");
        result[0].DownloadUrl.Should().Contain("2026-06");
    }

    [Fact]
    public async Task GetIndustryReports_WithReports_PeriodExtractedCorrectly()
    {
        IR2Service r2 = Substitute.For<IR2Service>();
        r2.ListByPrefixAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<R2ObjectInfo>>(new[]
            {
                new R2ObjectInfo("reports/industry/2026-06.json", DateTime.UtcNow, 512L),
            }));
        r2.GeneratePresignedReadUrlAsync(Arg.Any<string>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns("https://r2.example.com/signed-url");

        GetIndustryReportsHandler handler = new(r2);
        IReadOnlyList<IndustryReportSummaryResponse> result =
            await handler.Handle(new GetIndustryReportsQuery(), default);

        result.Should().HaveCount(1);
        result[0].Period.Should().Be("2026-06");
        result[0].DownloadUrl.Should().Be("https://r2.example.com/signed-url");
    }

    // ── IndustryReportJob — uploads to R2 ─────────────────────────────────────────

    [Fact]
    public async Task IndustryReportJob_Run_UploadsJsonToR2()
    {
        await SeedActiveStudios(count: 2); // < cohort min — metrics will be null

        byte[]? captured = null;
        string? capturedKey = null;

        IR2Service r2 = Substitute.For<IR2Service>();
        r2.When(x => x.UploadAsync(
                Arg.Any<string>(), Arg.Any<byte[]>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(c =>
            {
                capturedKey  = c.ArgAt<string>(0);
                captured     = c.ArgAt<byte[]>(1);
            });

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        IndustryReportJob job = new(db, r2);
        await job.RunAsync();

        captured.Should().NotBeNullOrEmpty();
        capturedKey.Should().StartWith("reports/industry/");
        capturedKey.Should().EndWith(".json");

        string json = Encoding.UTF8.GetString(captured!);
        json.Should().Contain("\"generated_at\"");
        json.Should().Contain("\"cohort_size\"");
        json.Should().Contain("\"period\"");
    }

    [Fact]
    public async Task IndustryReportJob_Run_JsonContainsNoIdentifiers()
    {
        await SeedActiveStudios(count: 2);

        byte[]? captured = null;
        IR2Service r2 = Substitute.For<IR2Service>();
        r2.When(x => x.UploadAsync(
                Arg.Any<string>(), Arg.Any<byte[]>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(c => captured = c.ArgAt<byte[]>(1));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        IndustryReportJob job = new(db, r2);
        await job.RunAsync();

        string json = Encoding.UTF8.GetString(captured!);
        json.Should().NotContain("studioId");
        json.Should().NotContain("userId");
        json.Should().NotContain("tenantId");
        json.Should().NotContain("email");
        json.Should().NotContain("studio_id");
        json.Should().NotContain("user_id");
    }

    [Fact]
    public async Task IndustryReportJob_Run_SmallCohort_MetricsAreNull()
    {
        await SeedActiveStudios(count: 3); // below MinCohortSize (10)

        byte[]? captured = null;
        IR2Service r2 = Substitute.For<IR2Service>();
        r2.When(x => x.UploadAsync(
                Arg.Any<string>(), Arg.Any<byte[]>(),
                Arg.Any<string>(), Arg.Any<CancellationToken>()))
            .Do(c => captured = c.ArgAt<byte[]>(1));

        await using AppDbContext db = fixture.CreateDbContext(Guid.Empty);
        IndustryReportJob job = new(db, r2);
        await job.RunAsync();

        string json = Encoding.UTF8.GetString(captured!);
        using JsonDocument doc = JsonDocument.Parse(json);

        JsonElement metrics = doc.RootElement.GetProperty("metrics");
        metrics.GetProperty("avg_appointments_per_studio_per_month").ValueKind.Should().Be(JsonValueKind.Null);
        metrics.GetProperty("peak_booking_hour_utc").ValueKind.Should().Be(JsonValueKind.Null);
        metrics.GetProperty("trial_to_paid_conversion_rate").ValueKind.Should().Be(JsonValueKind.Null);
        metrics.GetProperty("avg_retention_months").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────

    private async Task SeedActiveStudios(int count)
    {
        await using AppDbContext seed = fixture.CreateDbContext(Guid.Empty);

        for (int i = 0; i < count; i++)
        {
            Plan plan = new() { Name = $"Plan {i}" };
            seed.Plans.Add(plan);

            Studio studio = new()
            {
                Name     = $"Industry Test Studio {Guid.NewGuid():N}",
                Slug     = "industry-" + Guid.NewGuid().ToString("N")[..8],
                City     = "Lisboa",
                IsActive = true,
            };
            seed.Studios.Add(studio);

            Subscription sub = new()
            {
                StudioId         = studio.Id,
                PlanId           = plan.Id,
                Status           = SubscriptionStatus.Active,
                TrialExpiresAt   = DateTime.UtcNow.AddDays(-7),
                CurrentPeriodEnd = DateTime.UtcNow.AddDays(23),
                GracePeriodEnd   = DateTime.UtcNow.AddDays(30),
            };
            seed.Subscriptions.Add(sub);
        }

        await seed.SaveChangesAsync();
    }
}
