using Amazon.Runtime;
using Amazon.S3;
using Hangfire;
using Hangfire.MySql;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.Infrastructure.Services.MailKit;
using Resend;
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

        services.AddScoped<SubscriptionCacheInvalidationInterceptor>();
        services.AddDbContext<AppDbContext>((sp, options) =>
            options.UseMySql(
                connectionString,
                new MySqlServerVersion(new Version(8, 4, 0)),
                mysql => mysql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .AddInterceptors(sp.GetRequiredService<SubscriptionCacheInvalidationInterceptor>()));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddIdentityCore<IdentityUser>(options =>
        {
            options.Password.RequireDigit = true;
            options.Password.RequiredLength = 8;
            options.Password.RequireNonAlphanumeric = false;
            options.Lockout.MaxFailedAccessAttempts = 5;
        })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddDefaultTokenProviders();

        // Default token lifespan is 1 day; tighten to 1 hour for password-reset and
        // email-confirmation links (both use the "Default" provider). A user whose
        // verification link expires can request a new one via /auth/resend-verification.
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(1));

        var redisConnectionString = configuration["Redis:ConnectionString"]!;
        services.AddStackExchangeRedisCache(options =>
            options.Configuration = redisConnectionString + ",abortConnect=false");
        services.AddSingleton<IConnectionMultiplexer>(
            ConnectionMultiplexer.Connect(redisConnectionString + ",abortConnect=false"));

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
        services.AddSingleton<Stripe.CustomerService>();
        services.AddSingleton<Stripe.SubscriptionService>();
        services.AddSingleton<Stripe.SubscriptionScheduleService>();
        services.AddSingleton<Stripe.Checkout.SessionService>();
        services.AddSingleton<Stripe.BillingPortal.SessionService>();
        services.AddSingleton<Stripe.CouponService>();
        services.AddSingleton<Stripe.BalanceService>();

        TwilioClient.Init(
            configuration["Twilio:AccountSid"]!,
            configuration["Twilio:AuthToken"]!);

        // Options-lambda overload, not the string overload — the latter throws eagerly on an
        // empty API key, which would crash startup in any environment where Resend isn't yet
        // configured (matches Stripe/Twilio here, which also don't validate at startup).
        services.AddResend(options => options.ApiToken = configuration["Resend:ApiKey"] ?? "");

        services.Configure<R2Options>(configuration.GetSection(R2Options.Section));
        R2Options r2Opts = configuration.GetSection(R2Options.Section).Get<R2Options>()!;
        if (!string.IsNullOrEmpty(r2Opts.AccountId))
        {
            AmazonS3Config s3Config = new()
            {
                ServiceURL = $"https://{r2Opts.AccountId}.r2.cloudflarestorage.com",
                ForcePathStyle = true
            };
            services.AddSingleton<IAmazonS3>(
                new AmazonS3Client(new BasicAWSCredentials(r2Opts.AccessKeyId, r2Opts.SecretAccessKey), s3Config));
            services.AddSingleton<IR2Service, R2Service>();
        }
        else
        {
            services.AddSingleton<IR2Service, NullR2Service>();
        }

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTenant, CurrentTenantService>();
        services.AddScoped<ICurrentUser, CurrentUserService>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IRealtimeNotifier, RealtimeNotifier>();
        services.AddScoped<IJobScheduler, JobScheduler>();
        services.AddScoped<ISlotLocker, SlotLocker>();
        services.AddScoped<IStripePaymentService, StripePaymentService>();
        services.AddScoped<IStripeBillingService, StripeBillingService>();
        services.AddScoped<IStripeDiscountService, StripeDiscountService>();
        services.AddScoped<IReferralRewardService, ReferralRewardService>();

        services.AddScoped<IPortableProfileService, PortableProfileService>();
        services.AddScoped<IQrCodeService, QrCodeService>();
        services.AddSingleton<IConsentFormPdfService, ConsentFormPdfService>();
        services.AddSingleton<IPaymentInvoiceService, PaymentInvoiceService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddScoped<INotificationPreferenceService, NotificationPreferenceService>();
        services.AddScoped<ISubscriptionAccessService, SubscriptionAccessService>();
        services.AddScoped<IPlanLimitService, PlanLimitService>();
        services.AddSingleton<IEmailRenderer, EmailRenderer>();
        services.AddSingleton<IAppSettings, AppSettings>();

        services.Configure<InstagramOptions>(configuration.GetSection(InstagramOptions.Section));
        services.AddHttpClient("Instagram");
        services.AddSingleton<ITokenEncryptor, AesTokenEncryptor>();
        services.AddSingleton<IInstagramStateSigner, InstagramStateSigner>();
        services.AddScoped<IInstagramService, InstagramService>();
        services.AddTransient<InstagramSyncJob>();

        services.Configure<GoogleOptions>(configuration.GetSection(GoogleOptions.Section));
        services.Configure<AppleOptions>(configuration.GetSection(AppleOptions.Section));
        services.AddHttpClient("OAuthJwks");
        services.AddScoped<IOAuthTokenValidator, OAuthTokenValidator>();

        return services;
    }
}
