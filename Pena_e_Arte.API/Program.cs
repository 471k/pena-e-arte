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

    builder.Services.AddApiAuthentication(builder.Configuration);
    builder.Services.AddApiAuthorization();
    builder.Services.AddApiOpenTelemetry(builder.Configuration);
    builder.Services.AddApiCors(builder.Configuration);
    builder.Services.AddApiRateLimiting();

    builder.Services.AddHealthChecks()
        .AddCheck<RedisHealthCheck>("redis",       tags: ["ready"])
        .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
        .AddCheck<StripeHealthCheck>("stripe",     tags: ["ready"]);

    WebApplication app = builder.Build();

    using (IServiceScope migrationScope = app.Services.CreateScope())
    {
        AppDbContext migDb = migrationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await migDb.Database.MigrateAsync();
    }

    await SeedRolesAsync(app);
    await DataSeeder.SeedAsync(app.Services);
    await StripeDemoSeeder.SeedAsync(app.Services, app.Configuration);

    using (IServiceScope jobScope = app.Services.CreateScope())
    {
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
    }

    // Without this, the API only sees the K8s/Nginx ingress IP in RemoteIpAddress,
    // so every client shares one rate-limit bucket. KnownNetworks/KnownProxies are
    // left empty (trust all proxies) — acceptable on a private cluster network;
    // tighten to the ingress CIDR in production.
    app.UseForwardedHeaders(new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
    {
        ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor
                         | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto,
    });

    app.UseMiddleware<RequestIdMiddleware>();
    app.UseSerilogRequestLogging(options =>
        options.EnrichDiagnosticContext = RequestLoggingEnrichment.Enrich);
    app.UseMiddleware<ExceptionMiddleware>();
    app.UseCors();
    app.UseRateLimiter();
    app.UseAuthentication();
    app.UseMiddleware<TenantMiddleware>();
    app.UseAuthorization();

    app.UseHangfireDashboard(
        builder.Configuration["Hangfire:DashboardPath"] ?? "/hangfire",
        new DashboardOptions { Authorization = [new HangfireDashboardAuthFilter()] });

    app.MapHub<ScheduleHub>("/hubs/schedule");
    app.MapHub<DesignHub>("/hubs/design");
    app.MapHub<NotificationHub>("/hubs/notification");
    app.MapHealthChecks("/health");
    app.MapHealthChecks("/health/live",  new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
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
    app.MapSavedImagesEndpoints();
    app.MapPublicDesignEndpoints();
    app.MapAuthEndpoints();
    app.MapAppointmentEndpoints();
    app.MapDepositRuleEndpoints();
    app.MapArtistEndpoints();
    app.MapInstagramEndpoints();
    app.MapInstagramCallbackEndpoint();
    app.MapClientEndpoints();
    app.MapDesignEndpoints();
    app.MapStudioEndpoints();
    app.MapBillingEndpoints();
    app.MapFormEndpoints();
    app.MapPaymentEndpoints();
    app.MapNotificationEndpoints();
    app.MapFileEndpoints();
    app.MapReferralEndpoints();
    app.MapPlatformEndpoints();
    app.MapFeedbackEndpoints();

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
