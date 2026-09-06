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
using Pena_e_Arte.Application.Persistence;
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
    // Reused below for Hangfire's recurring-job registration too: runs once, via the
    // migration Job only, never in an API replica — kept even though the schema itself no
    // longer races (see Migrations/20260904203339_AddHangfireSchema.cs and
    // MySqlStorageOptions.PrepareSchemaIfNecessary = false: Hangfire.MySqlStorage's own
    // lazy table-creation, with no cross-process locking, is disabled entirely now).
    // Original diagnosis from this cluster's first production deploy was incomplete — it
    // looked like a pure multi-replica race (two API replicas calling
    // IRecurringJobManager.AddOrUpdate concurrently, one process's "tables already exist"
    // check short-circuiting before another finished), but a single replica, zero
    // concurrency, hit the exact same missing-hangfire_DistributedLock failure on every
    // retry — the real cause was the library's own Install.sql defining that table with no
    // primary key at all, which DigitalOcean Managed MySQL's default
    // sql_require_primary_key=ON rejects deterministically. Found 2026-09-04; see that
    // migration's doc comment for the full story.
    bool isOneTimeSetupRun = builder.Configuration.GetValue("Migrations:ApplyOnStartup", defaultValue: true);

    if (isOneTimeSetupRun)
    {
        using IServiceScope migrationScope = app.Services.CreateScope();
        AppDbContext migDb = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await migDb.Database.MigrateAsync();
    }

    await SeedRolesAsync(app);

    // Unconditional, unlike the rest of DataSeeder below — Free/Starter/Growth/Premium/Pro
    // are baseline product data every environment needs, not demo data.
    // RegisterSoloArtistCommand hard-depends on a Plan named "Free" existing; bundling this
    // reconciler behind Seeding:Enabled (never true in production) left every real database
    // with zero Plan rows and solo-artist registration permanently 500ing — found 2026-09-04.
    using (IServiceScope planScope = app.Services.CreateScope())
    {
        IAppDbContext planDb = planScope.ServiceProvider.GetRequiredService<IAppDbContext>();
        await DataSeeder.ReconcileCoreTiersAsync(planDb);
    }

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

        recurringJobs.AddOrUpdate<R2ExportJob>(
            "r2-export",
            j => j.RunAsync(CancellationToken.None),
            Cron.Daily(hour: 6)); // staggered after guest-pending-upload-cleanup (5am)
    }

    // k8s/base/migration-job.yaml runs this exact image as a one-off Job (restartPolicy:
    // Never) expecting the container to exit 0 once the block above finishes — but nothing
    // above this point ever did that. Every code path below runs app.Run(), which blocks
    // forever, so a migration Job that boots cleanly never completes; `kubectl wait
    // --for=condition=complete` just times out. The only reason this looked like it worked in
    // the past was almost certainly a startup exception being caught by this file's own
    // top-level try/catch (below), logged as Fatal, and then falling off the end of Program.cs
    // with a real exit code of 0 anyway — an accidental "success" that never actually proved
    // migrations/seeding ran, is indistinguishable from a genuinely completed Job, and stops
    // looking accidental the moment startup succeeds cleanly, as it does now. Found 2026-09-04
    // when a real migration hung a live production deploy. Migrations__ExitAfterMigrate is set
    // only by migration-job.yaml, never by the API Deployment's own ConfigMap.
    if (builder.Configuration.GetValue<bool>("Migrations:ExitAfterMigrate"))
    {
        Log.Information("Migration Job: setup complete, exiting.");
        return;
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

    foreach (string role in new[] { "client", "artist", "owner", "admin" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }
}

public partial class Program { }
