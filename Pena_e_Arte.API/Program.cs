using System.Reflection;
using FluentValidation;
using Hangfire;
using MediatR;
using Pena_e_Arte.Infrastructure.Jobs;
using Microsoft.AspNetCore.Identity;
using Pena_e_Arte.API.Endpoints;
using Pena_e_Arte.API.Extensions;
using Pena_e_Arte.API.Middleware;
using Pena_e_Arte.Application.Common.Behaviors;
using Pena_e_Arte.Infrastructure.Extensions;
using Pena_e_Arte.Infrastructure.Hubs;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Persistence.Seed;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, services, logConfig) =>
        logConfig
            .ReadFrom.Configuration(context.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext());

    builder.Services.AddInfrastructure(builder.Configuration);

    Assembly applicationAssembly = typeof(ValidationBehavior<,>).Assembly;
    builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(applicationAssembly));
    builder.Services.AddValidatorsFromAssembly(applicationAssembly);
    builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
    builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(PlanLimitBehavior<,>));
    builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(AuditLogBehavior<,>));

    builder.Services.AddApiAuthentication(builder.Configuration);
    builder.Services.AddApiAuthorization();
    builder.Services.AddApiOpenTelemetry(builder.Configuration);
    builder.Services.AddApiCors(builder.Configuration, builder.Environment);
    builder.Services.AddApiRateLimiting();

    Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder healthChecksBuilder = builder.Services.AddHealthChecks()
        .AddCheck<RedisHealthCheck>("redis", tags: ["ready"])
        .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

    // Skip registering the Stripe check entirely when no key is configured, rather than
    // letting it report Unhealthy — Cash deposits are a fully independent code path, so an
    // unconfigured Stripe key should not block pod readiness / the whole deployment.
    if (!string.IsNullOrWhiteSpace(builder.Configuration["Stripe:SecretKey"]))
        healthChecksBuilder.AddCheck<StripeHealthCheck>("stripe", tags: ["ready"]);

    WebApplication app = builder.Build();

    // Gated behind Migrations:ApplyOnStartup (default true, so local dotnet run / docker
    // compose behavior is unchanged) — with 2+ K8s replicas rolling out simultaneously,
    // every pod running MigrateAsync() unguarded races on the same migration history table.
    // Production sets this to false and runs exactly one migration via a dedicated K8s Job
    // (k8s/base/migration-job.yaml) before the API Deployment rolls out.
    //
    // Reused below for Hangfire's recurring-job registration too: Hangfire.MySql lazily
    // auto-creates its own schema tables on first use, with no cross-process locking of its
    // own — the exact same race MigrateAsync() would have had unguarded. Confirmed on this
    // cluster's real first production deploy: two API replicas (plus a still-restarting stale
    // pod from an earlier failed rollout) all called IRecurringJobManager.AddOrUpdate
    // concurrently, one process's "tables already exist, skip install" check short-circuited
    // before another process had finished creating every table, leaving
    // hangfire_DistributedLock missing and the next AcquireDistributedLock call crashing with
    // MySqlException. Fixed the same way as MigrateAsync(): runs once, in the migration Job
    // only, never in an API replica.
    bool isOneTimeSetupRun = builder.Configuration.GetValue("Migrations:ApplyOnStartup", defaultValue: true);

    if (isOneTimeSetupRun)
    {
        using IServiceScope migrationScope = app.Services.CreateScope();
        AppDbContext migDb = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await migDb.Database.MigrateAsync();
    }

    await SeedRolesAsync(app);

    // DataSeeder upserts demo accounts with known passwords (Password123) and, on a
    // fresh database, whole fake studios/subscriptions/appointments — must never run
    // against a real production database. Opt-in via config, not IsDevelopment(), so
    // a staging environment can still enable it without matching "Development".
    if (builder.Configuration.GetValue<bool>("Seeding:Enabled"))
        await DataSeeder.SeedAsync(app.Services);

    // Self-guarded: only provisions Stripe objects when Stripe:SecretKey is a
    // sk_test_ key, so it's inert against a live production key regardless of
    // Seeding:Enabled.
    await StripeDemoSeeder.SeedAsync(app.Services, app.Configuration);

    if (isOneTimeSetupRun)
    {
        using IServiceScope jobScope = app.Services.CreateScope();
        IRecurringJobManager recurringJobs = jobScope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
        recurringJobs.AddOrUpdate<IndustryReportJob>(
            "industry-report",
            j => j.RunAsync(CancellationToken.None),
            Cron.Monthly());

        recurringJobs.AddOrUpdate<PaymentReconciliationJob>(
            "payment-reconciliation",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(2));

        recurringJobs.AddOrUpdate<InstagramSyncJob>(
            "instagram-nightly-sync",
            j => j.ExecuteAsync(CancellationToken.None),
            Cron.Daily(hour: 3));

        recurringJobs.AddOrUpdate<TrafficRollupJob>(
            "traffic-rollup",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(hour: 2, minute: 30));

        recurringJobs.AddOrUpdate<RetentionPurgeJob>(
            "retention-purge",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(hour: 4)); // staggered away from reconciliation (2am) and instagram-sync (3am)

        recurringJobs.AddOrUpdate<GuestPendingUploadCleanupJob>(
            "guest-pending-upload-cleanup",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(hour: 5)); // staggered after retention-purge (4am)
    }

    // Without a configured TrustedProxyCidr, the .NET runtime's own forwarded-headers hardening
    // (see ForwardedHeadersOptionsBuilder) ignores X-Forwarded-For entirely, so the API sees only
    // the K3s/Nginx ingress IP and every client shares one rate-limit bucket. Setting
    // ForwardedHeaders:TrustedProxyCidr to the ingress CIDR in production fixes that while still
    // rejecting a spoofed header from any untrusted direct client.
    app.UseForwardedHeaders(ForwardedHeadersOptionsBuilder.BuildForwardedHeadersOptions(
        app.Configuration, app.Logger));

    app.UseMiddleware<RequestIdMiddleware>();
    app.UseSerilogRequestLogging(options =>
        options.EnrichDiagnosticContext = RequestLoggingEnrichment.Enrich);
    app.UseMiddleware<ExceptionMiddleware>();
    app.UseCors();
    // UseRateLimiter must come after UseAuthentication: the "billing" policy (Phase 7 of the
    // 2026-07-26 security remediation) partitions by the caller's user-id claim, which isn't
    // populated on HttpContext.User until authentication middleware runs — verified empirically,
    // since the "auth"/"public-write"/"public-read" policies (IP-keyed, unaffected by this order)
    // predate that requirement. Moving it does not change those three policies' behavior.
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseMiddleware<TenantMiddleware>();
    app.UseAuthorization();

    app.UseHangfireDashboard(
        builder.Configuration["Hangfire:DashboardPath"] ?? "/hangfire",
        new DashboardOptions { Authorization = [new HangfireDashboardAuthFilter(builder.Configuration)] });

    app.MapHub<ScheduleHub>("/hubs/schedule");
    app.MapHub<DesignHub>("/hubs/design");
    app.MapHub<NotificationHub>("/hubs/notification");
    app.MapHub<SupportHub>("/hubs/support");
    app.MapHub<TrafficHub>("/hubs/traffic");
    app.MapHub<ChatHub>("/hubs/chat");
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = _ => false
    });
    app.MapHealthChecks("/health/ready", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains("ready"),
        // Add this if Stripe rate limits become a concern at high pod counts:
        // MaximumAge = TimeSpan.FromSeconds(30),
    });
    app.MapPrometheusScrapingEndpoint();

    app.MapPublicEndpoints();
    app.MapContactEndpoints();
    app.MapSavedImagesEndpoints();
    app.MapPublicDesignEndpoints();
    app.MapAuthEndpoints();
    app.MapAppointmentEndpoints();
    app.MapDepositRuleEndpoints();
    app.MapArtistEndpoints();
    app.MapInstagramEndpoints();
    app.MapInstagramCallbackEndpoint();
    app.MapSocialEndpoints();
    app.MapSocialCallbackEndpoint();
    app.MapClientEndpoints();
    app.MapDesignEndpoints();
    app.MapStudioEndpoints();
    app.MapBillingEndpoints();
    app.MapFormEndpoints();
    app.MapPaymentEndpoints();
    app.MapNotificationEndpoints();
    app.MapManualReminderEndpoints();
    app.MapFileEndpoints();
    app.MapReferralEndpoints();
    app.MapPlatformEndpoints();
    app.MapFeedbackEndpoints();
    app.MapReviewEndpoints();
    app.MapHelpEndpoints();
    app.MapReportEndpoints();
    app.MapConductReportEndpoints();
    app.MapMessagingEndpoints();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Application startup failed");
}
finally
{
    Log.CloseAndFlush();
}

static async Task SeedRolesAsync(WebApplication app)
{
    using IServiceScope scope = app.Services.CreateScope();
    RoleManager<IdentityRole> roleManager =
        scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

    foreach (string role in new[] { "client", "artist", "owner", "issuer" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }
}

public partial class Program { }
