using Amazon.Runtime;
using Amazon.S3;
using Hangfire;
using Hangfire.MySql;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Jobs;
using Pena_e_Arte.Infrastructure.Persistence;
using Pena_e_Arte.Infrastructure.Services;
using Pena_e_Arte.Infrastructure.Services.MailKit;
using Pena_e_Arte.Infrastructure.Services.Social;
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

        // IAppDbContextFactory hands out independent IAppDbContext instances for code that needs
        // to run several queries concurrently (a single DbContext can't serve overlapping
        // operations — see GetTrafficBreakdownQuery). EF Core's own IDbContextFactory<T> can't be
        // used here: it resolves against the root provider, so it can't inject AppDbContext's
        // scoped constructor dependencies (ICurrentTenant, the interceptor above) — confirmed via
        // a real startup failure, not a theoretical concern. AppDbContextRuntimeFactory instead
        // opens a genuine new DI scope per call via IServiceScopeFactory, so AppDbContext gets
        // built exactly like it is for a normal HTTP request.
        services.AddSingleton<IAppDbContextFactory, AppDbContextRuntimeFactory>();

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

        services.AddHangfire((serviceProvider, config) => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseStorage(new MySqlStorage(connectionString, new MySqlStorageOptions
            {
                TablesPrefix = "hangfire_",
                // Hangfire.MySqlStorage 2.0.3's own installer defines hangfire_DistributedLock
                // with NO primary key at all — DigitalOcean Managed MySQL enforces
                // sql_require_primary_key=ON by default (most local/CI MySQL doesn't), which
                // rejects that exact CREATE TABLE deterministically, every time, leaving the
                // schema permanently stuck 3 tables short. Invisible to any test suite whose
                // MySQL uses default settings. The corrected schema (a real migration, not this
                // library) now owns table creation — see
                // Migrations/20260904210000_AddHangfireSchema.cs. Found and fixed 2026-09-04.
                PrepareSchemaIfNecessary = false
            }))
            .UseFilter(new HangfireJobFailureLogFilter(
                serviceProvider.GetRequiredService<ILogger<HangfireJobFailureLogFilter>>())));
        services.AddHangfireServer();

        services.AddSignalR();

        // Stripe.net stays for Flow B (billing/subscriptions) only. The Flow-A payment-intent /
        // refund services were removed with the deleted Stripe aggregator payment service.
        Stripe.StripeConfiguration.ApiKey = configuration["Stripe:SecretKey"]!;
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
            services.AddSingleton<IR2ExportService, R2ExportService>();
        }
        else
        {
            services.AddSingleton<IR2Service, NullR2Service>();
            services.AddSingleton<IR2ExportService, NullR2ExportService>();
        }
        services.AddTransient<R2ExportJob>();

        services.Configure<RetentionOptions>(configuration.GetSection(RetentionOptions.Section));
        services.AddTransient<RetentionPurgeJob>();
        services.AddTransient<GuestPendingUploadCleanupJob>();

        // Secrets backend (Vault by default — see docs/infra/ADR-0002-secrets-management.md).
        // Construction does not connect; a call resolves against Vault:Address at use time and
        // fails closed if it can't. Registered always — nothing consumes it yet (per-tenant
        // provider credentials are ADR-0001 follow-up work; this is the mechanism only).
        services.Configure<VaultOptions>(configuration.GetSection(VaultOptions.Section));
        services.AddSingleton<ISecretsProvider, VaultSecretsProvider>();

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentTenant, CurrentTenantService>();
        services.AddScoped<ICurrentUser, CurrentUserService>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IRealtimeNotifier, RealtimeNotifier>();
        services.AddScoped<IJobScheduler, JobScheduler>();
        services.AddScoped<ISlotLocker, SlotLocker>();
        // Flow A card provider: NullPaymentProvider (fails closed) until POK is wired in — the
        // Stripe aggregator IStripePaymentService/StripePaymentService were deleted (Amendment A).
        services.AddScoped<IPaymentProvider, NullPaymentProvider>();
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
        services.AddScoped<IManualReminderQuotaService, ManualReminderQuotaService>();
        services.AddSingleton<IEmailRenderer, EmailRenderer>();
        services.AddSingleton<IAppSettings, AppSettings>();

        services.Configure<InstagramOptions>(configuration.GetSection(InstagramOptions.Section));
        services.AddHttpClient("Instagram");
        services.AddSingleton<ITokenEncryptor, AesTokenEncryptor>();
        services.AddSingleton<IInstagramStateSigner, InstagramStateSigner>();
        services.AddScoped<IInstagramService, InstagramService>();
        services.AddTransient<InstagramSyncJob>();

        // Social verification (Instagram/TikTok/Facebook/X/YouTube) — config-gated, see
        // ISocialOAuthProvider.IsConfigured. Instagram's provider/checker wrap the
        // InstagramOptions/IInstagramService already registered above rather than
        // duplicating them.
        services.Configure<SocialSigningOptions>(configuration.GetSection(SocialSigningOptions.Section));
        services.Configure<TikTokOptions>(configuration.GetSection(TikTokOptions.Section));
        services.Configure<FacebookOptions>(configuration.GetSection(FacebookOptions.Section));
        services.Configure<XOptions>(configuration.GetSection(XOptions.Section));
        services.Configure<YouTubeOptions>(configuration.GetSection(YouTubeOptions.Section));
        services.AddHttpClient("TikTok");
        services.AddHttpClient("Facebook");
        services.AddHttpClient("X");
        services.AddHttpClient("YouTube");
        services.AddSingleton<ISocialOAuthStateSigner, SocialOAuthStateSigner>();

        services.AddScoped<ISocialOAuthProvider, InstagramSocialOAuthProvider>();
        services.AddScoped<ISocialOAuthProvider, TikTokSocialOAuthProvider>();
        services.AddScoped<ISocialOAuthProvider, FacebookSocialOAuthProvider>();
        services.AddScoped<ISocialOAuthProvider, XSocialOAuthProvider>();
        services.AddScoped<ISocialOAuthProvider, YouTubeSocialOAuthProvider>();
        services.AddScoped<ISocialOAuthProviderFactory, SocialOAuthProviderFactory>();

        services.AddScoped<ISocialBioChecker, InstagramBioChecker>();
        services.AddScoped<ISocialBioChecker, TikTokBioChecker>();
        services.AddScoped<ISocialBioChecker, FacebookBioChecker>();
        services.AddScoped<ISocialBioChecker, XBioChecker>();
        services.AddScoped<ISocialBioChecker, YouTubeBioChecker>();
        services.AddScoped<ISocialBioCheckerFactory, SocialBioCheckerFactory>();

        services.Configure<GoogleOptions>(configuration.GetSection(GoogleOptions.Section));
        services.Configure<AppleOptions>(configuration.GetSection(AppleOptions.Section));
        services.AddHttpClient("OAuthJwks");
        services.AddScoped<IOAuthTokenValidator, OAuthTokenValidator>();

        // GeoIpService/UserAgentParserService are stateless wrappers over thread-safe underlying
        // readers/parsers (DatabaseReader, UAParser.Parser) — safe as singletons.
        services.AddSingleton<IGeoIpService, GeoIpService>();
        services.AddSingleton<IUserAgentParser, UserAgentParserService>();
        services.AddSingleton<ITrafficConnectionCounter, TrafficConnectionCounter>();
        services.AddScoped<ITrafficPresenceReader, TrafficPresenceService>();
        services.AddHostedService<TrafficBroadcastService>();

        return services;
    }
}
