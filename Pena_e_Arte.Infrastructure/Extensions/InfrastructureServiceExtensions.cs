using Hangfire;
using Hangfire.MySql;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using StackExchange.Redis;
using Twilio;

namespace Pena_e_Arte.Infrastructure.Extensions;

public static class InfrastructureServiceExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default")!;

        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(
                connectionString,
                new MySqlServerVersion(new Version(8, 4, 0)),
                mysql => mysql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentityCore<IdentityUser>(options =>
        {
            options.Password.RequireDigit           = true;
            options.Password.RequiredLength         = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>();

        var redisConnectionString = configuration["Redis:ConnectionString"]!;
        services.AddStackExchangeRedisCache(options =>
            options.Configuration = redisConnectionString);
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConnectionString));

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseStorage(new MySqlStorage(connectionString, new MySqlStorageOptions
            {
                TablesPrefix = "hangfire_"
            })));
        services.AddHangfireServer();

        services.AddSignalR();

        Stripe.StripeConfiguration.ApiKey = configuration["Stripe:SecretKey"]!;
        services.AddSingleton<Stripe.PaymentIntentService>();
        services.AddSingleton<Stripe.RefundService>();
        services.AddSingleton<Stripe.AccountService>();
        services.AddSingleton<Stripe.AccountLinkService>();

        TwilioClient.Init(
            configuration["Twilio:AccountSid"]!,
            configuration["Twilio:AuthToken"]!);

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTenant,        CurrentTenantService>();
        services.AddScoped<ICurrentUser,          CurrentUserService>();
        services.AddScoped<IIdentityService,      IdentityService>();
        services.AddScoped<IRealtimeNotifier,     RealtimeNotifier>();
        services.AddScoped<IJobScheduler,         JobScheduler>();
        services.AddScoped<ISlotLocker,           SlotLocker>();
        services.AddScoped<IStripePaymentService, StripePaymentService>();
        services.AddScoped<IStripeConnectService, StripeConnectService>();
        services.AddScoped<INotificationService,  NotificationService>();

        return services;
    }
}
