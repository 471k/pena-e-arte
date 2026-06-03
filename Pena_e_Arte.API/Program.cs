using System.Reflection;
using FluentValidation;
using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Pena_e_Arte.API.Endpoints;
using Pena_e_Arte.API.Extensions;
using Pena_e_Arte.API.Middleware;
using Pena_e_Arte.Application.Common.Behaviors;
using Pena_e_Arte.Infrastructure.Extensions;
using Pena_e_Arte.Infrastructure.Hubs;
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

    builder.Services.AddCors(options =>
        options.AddDefaultPolicy(policy =>
            policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));

    builder.Services.AddHealthChecks();

    WebApplication app = builder.Build();

    await SeedRolesAsync(app);

    app.UseSerilogRequestLogging();
    app.UseMiddleware<ExceptionMiddleware>();
    app.UseCors();
    app.UseAuthentication();
    app.UseMiddleware<TenantMiddleware>();
    app.UseAuthorization();

    app.UseHangfireDashboard(
        builder.Configuration["Hangfire:DashboardPath"] ?? "/hangfire",
        new DashboardOptions { Authorization = [new HangfireDashboardAuthFilter()] });

    app.MapHub<ScheduleHub>("/hubs/schedule");
    app.MapHealthChecks("/health");
    app.MapPrometheusScrapingEndpoint();

    app.MapAuthEndpoints();
    app.MapAppointmentEndpoints();
    app.MapDepositRuleEndpoints();
    app.MapArtistEndpoints();
    app.MapClientEndpoints();
    app.MapDesignEndpoints();
    app.MapStudioEndpoints();
    app.MapBillingEndpoints();
    app.MapFormEndpoints();
    app.MapPaymentEndpoints();
    app.MapNotificationEndpoints();

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
