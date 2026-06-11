# Payment Architecture Overhaul — Overnight Execution Prompt

> **Context:** Stripe Connect Express does not support the platform owner's country.
> All client payments are currently broken because `StripePaymentService` routes every
> `PaymentIntent` to a connected account via the `StripeAccount` header. This prompt
> fixes that completely and adds PayPal as both a client-facing payment option and the
> primary studio payout mechanism.
>
> Read `CLAUDE.md`, `docs/claude/backend.md`, `docs/claude/frontend.md`,
> `docs/claude/database.md`, and `docs/claude/conventions.md` before starting.
> Execute sections in order — each section builds on the previous one.

---

## Root Cause & Architecture Decision

**Before (broken):** Marketplace model. Every `PaymentIntent` created with
`RequestOptions { StripeAccount = studio.StripeAccountId }`. Studios must onboard
via Stripe Connect. Studio payouts are automatic from Stripe to the connected account.

**After (fixed):** Aggregator model. Platform's own Stripe account collects all client
payments directly. No `StripeAccount` header anywhere. Studio payouts are decoupled —
handled via PayPal Payouts API (widely supported, including Albania). PayPal Checkout
added as a second client-facing payment method alongside Stripe.

```
CLIENT PAYMENTS (aggregator, into platform Stripe account)
  Option A: Stripe Payment Element  → cards, Apple Pay, Google Pay
  Option B: PayPal Checkout         → PayPal balance, cards via PayPal

STUDIO PAYOUTS (platform → studio, after session)
  Primary:  PayPal Payouts API      → studio's registered PayPal email
  Fallback: Manual bank transfer    → issuer manually marks as paid

PLATFORM SUBSCRIPTIONS (studio → platform, unchanged)
  Stripe Billing                    → no change
```

---

## ⚠️ Security Rules Before Starting

1. **Never commit secrets.** Stripe keys and PayPal credentials go in
   `appsettings.Development.json` (gitignored) and environment variables only.
2. **Never hardcode** `pk_test_...`, `sk_test_...`, or any PayPal credential in source.
3. Frontend env vars go in `.env.local` (gitignored), prefixed `VITE_`.
4. The required env var names are documented at the end of this prompt.

---

## Global Rules (enforced on every file)

- No business logic in endpoints — MediatR only.
- Every DB query on tenant data through EF Core global query filters.
- No PII in logs. Structured Serilog with `@` prefix on all properties.
- TypeScript strict mode. No `any`. Named exports only on components.
- No `useEffect` for data fetching — RTK Query only.
- Write tests alongside every handler. No skipping.

---

## New NuGet Packages

Add to `Pena_e_Arte.Infrastructure/Pena_e_Arte.Infrastructure.csproj`:

```xml
<!-- PayPal REST API v2 calls via typed HttpClient — no SDK needed -->
<!-- HttpClient is already available in ASP.NET Core -->
```

No new NuGet package needed. Use `IHttpClientFactory` with a named `"PayPal"` client.
PayPal's REST API v2 is a simple OAuth 2.0 + JSON API. Wrapping it ourselves is cleaner
than pulling in a poorly-maintained SDK and keeps the dependency count low.

## New npm Package

Add to `frontend/package.json` (run `pnpm add @paypal/react-paypal-js`):

```
@paypal/react-paypal-js   — official PayPal React SDK (PayPal Checkout button)
```

---

## SECTION 1 — Domain Layer

### 1.1 New Enum: `PayoutMethodType`

**Create** `Pena_e_Arte.Domain/Enums/PayoutMethodType.cs`:

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum PayoutMethodType
{
    PayPal,
    BankTransfer,
}
```

### 1.2 New Enum: `PayoutStatus`

**Create** `Pena_e_Arte.Domain/Enums/PayoutStatus.cs`:

```csharp
namespace Pena_e_Arte.Domain.Enums;

public enum PayoutStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
}
```

### 1.3 New Enum: `ClientPaymentMethod`

**Create** `Pena_e_Arte.Domain/Enums/ClientPaymentMethod.cs`:

```csharp
namespace Pena_e_Arte.Domain.Enums;

/// <summary>Which payment provider the client used to pay.</summary>
public enum ClientPaymentMethod
{
    Stripe,
    PayPal,
}
```

### 1.4 New Entity: `StudioPayoutMethod`

**Create** `Pena_e_Arte.Domain/Entities/StudioPayoutMethod.cs`:

```csharp
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// How a studio wants to receive payouts from the platform.
/// One record per studio (upsert, not append).
/// </summary>
public class StudioPayoutMethod : TenantEntity
{
    public PayoutMethodType Type           { get; set; }
    public string?          PayPalEmail    { get; set; }
    public string?          BankHolder     { get; set; }
    public string?          BankIban       { get; set; }
    public string?          BankSwift      { get; set; }
    public bool             IsVerified     { get; set; } = false;
    public DateTime         CreatedAt      { get; init; } = DateTime.UtcNow;
    public DateTime         UpdatedAt      { get; set; } = DateTime.UtcNow;
}
```

### 1.5 New Entity: `StudioPayout`

**Create** `Pena_e_Arte.Domain/Entities/StudioPayout.cs`:

```csharp
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

/// <summary>
/// A single payout from the platform to a studio after a completed session.
/// </summary>
public class StudioPayout : TenantEntity
{
    public Guid          PaymentId          { get; set; }
    public decimal       Amount             { get; set; }
    public string        Currency           { get; set; } = "EUR";
    public PayoutStatus  Status             { get; set; } = PayoutStatus.Pending;
    public PayoutMethodType Method          { get; set; }

    // PayPal Payouts fields
    public string? PayPalBatchId           { get; set; }
    public string? PayPalPayoutItemId      { get; set; }

    // Failure info
    public string? FailureReason           { get; set; }

    public DateTime  CreatedAt             { get; init; } = DateTime.UtcNow;
    public DateTime? ProcessedAt           { get; set; }
    public DateTime? CompletedAt           { get; set; }

    public Payment Payment { get; set; } = null!;
}
```

### 1.6 Update Entity: `Payment`

**Edit** `Pena_e_Arte.Domain/Entities/Payment.cs` — add PayPal fields and method tracking:

```csharp
using Pena_e_Arte.Domain.Enums;

namespace Pena_e_Arte.Domain.Entities;

public class Payment : TenantEntity
{
    public Guid             AppointmentId         { get; set; }
    public Guid             ClientId              { get; set; }
    public decimal          Amount                { get; set; }
    public PaymentStatus    Status                { get; set; }
    public ClientPaymentMethod Method             { get; set; } = ClientPaymentMethod.Stripe;

    // Stripe fields
    public string? StripePaymentIntentId         { get; set; }
    public string? ClientSecret                  { get; set; }

    // PayPal fields
    public string? PayPalOrderId                 { get; set; }
    public string? PayPalCaptureId               { get; set; }

    public DateTime?  PaidAt                     { get; set; }

    public Appointment Appointment               { get; set; } = null!;
    public Client      Client                    { get; set; } = null!;
    public ICollection<SessionSplit> SessionSplits { get; set; } = [];
    public StudioPayout? Payout                  { get; set; }
}
```

### 1.7 New Domain Interfaces

**Create** `Pena_e_Arte.Domain/Interfaces/IStripePaymentService.cs` (replace existing):

```csharp
namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Aggregator model: all PaymentIntents go directly to the platform's Stripe account.
/// No connected account headers.
/// </summary>
public interface IStripePaymentService
{
    Task<(string PaymentIntentId, string ClientSecret)> CreatePaymentIntentAsync(
        long amountInCents, string currency, Guid paymentId, CancellationToken ct);

    Task CapturePaymentAsync(string paymentIntentId, CancellationToken ct);

    Task<string> RefundPaymentIntentAsync(
        string paymentIntentId, long? amountInCents, CancellationToken ct);
}
```

**Create** `Pena_e_Arte.Domain/Interfaces/IPayPalCheckoutService.cs`:

```csharp
namespace Pena_e_Arte.Domain.Interfaces;

public record PayPalOrderResult(string OrderId, string ApproveUrl);
public record PayPalCaptureResult(string CaptureId, string Status, decimal Amount);

public interface IPayPalCheckoutService
{
    /// <summary>Creates a PayPal Order and returns the order ID + approval URL.</summary>
    Task<PayPalOrderResult> CreateOrderAsync(
        decimal amount, string currency, Guid paymentId, CancellationToken ct);

    /// <summary>Captures an approved PayPal Order. Returns capture details.</summary>
    Task<PayPalCaptureResult> CaptureOrderAsync(string orderId, CancellationToken ct);

    /// <summary>Refunds a captured PayPal payment.</summary>
    Task RefundCaptureAsync(string captureId, decimal? amount, CancellationToken ct);
}
```

**Create** `Pena_e_Arte.Domain/Interfaces/IPayPalPayoutService.cs`:

```csharp
namespace Pena_e_Arte.Domain.Interfaces;

public record PayPalPayoutResult(string BatchId, string BatchStatus);

public interface IPayPalPayoutService
{
    /// <summary>
    /// Sends a single payout to a PayPal email address.
    /// Returns the PayPal batch ID for tracking.
    /// </summary>
    Task<PayPalPayoutResult> SendPayoutAsync(
        string recipientEmail,
        decimal amount,
        string currency,
        string senderItemId,
        string note,
        CancellationToken ct);

    /// <summary>Retrieves the current status of a payout batch.</summary>
    Task<string> GetBatchStatusAsync(string batchId, CancellationToken ct);
}
```

---

## SECTION 2 — Infrastructure Layer

### 2.1 Fix `StripePaymentService` (Aggregator Model)

**Replace** `Pena_e_Arte.Infrastructure/Services/StripePaymentService.cs` entirely:

```csharp
using Pena_e_Arte.Domain.Interfaces;
using Stripe;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Aggregator model: all charges collected into the platform's own Stripe account.
/// No StripeAccount (connected account) header is sent.
/// </summary>
public class StripePaymentService(PaymentIntentService intentService, RefundService refundService)
    : IStripePaymentService
{
    public async Task<(string PaymentIntentId, string ClientSecret)> CreatePaymentIntentAsync(
        long amountInCents, string currency, Guid paymentId, CancellationToken ct)
    {
        PaymentIntentCreateOptions options = new()
        {
            Amount   = amountInCents,
            Currency = currency.ToLowerInvariant(),
            CaptureMethod = "manual",
            Metadata = new Dictionary<string, string> { { "payment_id", paymentId.ToString() } },
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions
            {
                Enabled = true,
            },
        };

        // No RequestOptions with StripeAccount — platform account only (aggregator model)
        PaymentIntent intent = await intentService.CreateAsync(options, null, ct);
        return (intent.Id, intent.ClientSecret!);
    }

    public async Task CapturePaymentAsync(string paymentIntentId, CancellationToken ct)
    {
        await intentService.CaptureAsync(paymentIntentId, null, null, ct);
    }

    public async Task<string> RefundPaymentIntentAsync(
        string paymentIntentId, long? amountInCents, CancellationToken ct)
    {
        RefundCreateOptions options = new()
        {
            PaymentIntent = paymentIntentId,
            Amount        = amountInCents,
        };

        Refund refund = await refundService.CreateAsync(options, null, ct);
        return refund.Id;
    }
}
```

### 2.2 Deprecate `StripeConnectService`

**Edit** `Pena_e_Arte.Infrastructure/Services/StripeConnectService.cs` — add `[Obsolete]`
and logging so it is clear this path is no longer active:

```csharp
using Pena_e_Arte.Domain.Interfaces;
using Stripe;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// DEPRECATED: Stripe Connect (marketplace model) is not available in the platform's
/// country. Retained for potential future use if Stripe expands coverage.
/// The platform now uses the aggregator model — see StripePaymentService.
/// </summary>
[Obsolete("Stripe Connect not supported in the platform's country. Use aggregator model.")]
public class StripeConnectService(AccountService accountService, AccountLinkService accountLinkService)
    : IStripeConnectService
{
    public Task<string> CreateConnectedAccountAsync(string email, string country, CancellationToken ct)
        => throw new NotSupportedException(
            "Stripe Connect is not available in this country. " +
            "Studio payouts are handled via PayPal Payouts instead.");

    public Task<string> CreateAccountLinkAsync(
        string accountId, string returnUrl, string refreshUrl, CancellationToken ct)
        => throw new NotSupportedException(
            "Stripe Connect is not available in this country.");
}
```

### 2.3 New `PayPalHttpClient` — Token + Request Helper

**Create** `Pena_e_Arte.Infrastructure/Services/PayPal/PayPalOptions.cs`:

```csharp
namespace Pena_e_Arte.Infrastructure.Services.PayPal;

public class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string ClientId     { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;

    /// <summary>
    /// "https://api-m.sandbox.paypal.com" for sandbox,
    /// "https://api-m.paypal.com" for production.
    /// </summary>
    public string BaseUrl      { get; init; } = "https://api-m.sandbox.paypal.com";

    public string WebhookId    { get; init; } = string.Empty;
}
```

**Create** `Pena_e_Arte.Infrastructure/Services/PayPal/PayPalTokenCache.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Pena_e_Arte.Infrastructure.Services.PayPal;

/// <summary>
/// Thread-safe singleton that caches the PayPal OAuth 2.0 access token
/// and refreshes it 60 seconds before expiry.
/// </summary>
public class PayPalTokenCache(IHttpClientFactory httpClientFactory, PayPalOptions options)
{
    private string?  _cachedToken;
    private DateTime _expiresAt = DateTime.MinValue;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<string> GetTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTime.UtcNow < _expiresAt.AddSeconds(-60))
            return _cachedToken;

        await _lock.WaitAsync(ct);
        try
        {
            // Double-check inside lock
            if (_cachedToken is not null && DateTime.UtcNow < _expiresAt.AddSeconds(-60))
                return _cachedToken;

            HttpClient client = httpClientFactory.CreateClient("PayPal");

            string credentials = Convert.ToBase64String(
                Encoding.ASCII.GetBytes($"{options.ClientId}:{options.ClientSecret}"));

            using HttpRequestMessage request = new(HttpMethod.Post, "/v1/oauth2/token");
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
            request.Content = new FormUrlEncodedContent(
                new[] { new KeyValuePair<string, string>("grant_type", "client_credentials") });

            using HttpResponseMessage response = await client.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();

            string json      = await response.Content.ReadAsStringAsync(ct);
            JsonDocument doc = JsonDocument.Parse(json);
            _cachedToken     = doc.RootElement.GetProperty("access_token").GetString()!;
            int expiresIn    = doc.RootElement.GetProperty("expires_in").GetInt32();
            _expiresAt       = DateTime.UtcNow.AddSeconds(expiresIn);

            return _cachedToken;
        }
        finally
        {
            _lock.Release();
        }
    }
}
```

### 2.4 New `PayPalCheckoutService`

**Create** `Pena_e_Arte.Infrastructure/Services/PayPal/PayPalCheckoutService.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.PayPal;

public class PayPalCheckoutService(
    IHttpClientFactory httpClientFactory,
    PayPalTokenCache   tokenCache,
    PayPalOptions      options)
    : IPayPalCheckoutService
{
    public async Task<PayPalOrderResult> CreateOrderAsync(
        decimal amount, string currency, Guid paymentId, CancellationToken ct)
    {
        string token  = await tokenCache.GetTokenAsync(ct);
        HttpClient client = httpClientFactory.CreateClient("PayPal");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        object body = new
        {
            intent             = "CAPTURE",
            purchase_units     = new[]
            {
                new
                {
                    reference_id = paymentId.ToString(),
                    amount       = new
                    {
                        currency_code = currency.ToUpperInvariant(),
                        value         = amount.ToString("F2"),
                    },
                },
            },
            payment_source = new
            {
                paypal = new
                {
                    experience_context = new
                    {
                        payment_method_preference = "IMMEDIATE_PAYMENT_REQUIRED",
                        user_action               = "PAY_NOW",
                    },
                },
            },
        };

        string json = JsonSerializer.Serialize(body);
        using HttpResponseMessage response = await client.PostAsync(
            "/v2/checkout/orders",
            new StringContent(json, Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();

        JsonDocument doc   = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        string orderId     = doc.RootElement.GetProperty("id").GetString()!;
        string approveUrl  = doc.RootElement
            .GetProperty("links")
            .EnumerateArray()
            .First(l => l.GetProperty("rel").GetString() == "payer-action")
            .GetProperty("href")
            .GetString()!;

        return new PayPalOrderResult(orderId, approveUrl);
    }

    public async Task<PayPalCaptureResult> CaptureOrderAsync(string orderId, CancellationToken ct)
    {
        string token  = await tokenCache.GetTokenAsync(ct);
        HttpClient client = httpClientFactory.CreateClient("PayPal");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await client.PostAsync(
            $"/v2/checkout/orders/{orderId}/capture",
            new StringContent("{}", Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();

        JsonDocument doc    = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        JsonElement capture = doc.RootElement
            .GetProperty("purchase_units")[0]
            .GetProperty("payments")
            .GetProperty("captures")[0];

        string  captureId = capture.GetProperty("id").GetString()!;
        string  status    = capture.GetProperty("status").GetString()!;
        decimal amount    = decimal.Parse(
            capture.GetProperty("amount").GetProperty("value").GetString()!);

        return new PayPalCaptureResult(captureId, status, amount);
    }

    public async Task RefundCaptureAsync(string captureId, decimal? amount, CancellationToken ct)
    {
        string token  = await tokenCache.GetTokenAsync(ct);
        HttpClient client = httpClientFactory.CreateClient("PayPal");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        object body = amount.HasValue
            ? new { amount = new { currency_code = "EUR", value = amount.Value.ToString("F2") } }
            : new { };

        using HttpResponseMessage response = await client.PostAsync(
            $"/v2/payments/captures/{captureId}/refund",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();
    }
}
```

### 2.5 New `PayPalPayoutService`

**Create** `Pena_e_Arte.Infrastructure/Services/PayPal/PayPalPayoutService.cs`:

```csharp
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Infrastructure.Services.PayPal;

public class PayPalPayoutService(
    IHttpClientFactory httpClientFactory,
    PayPalTokenCache   tokenCache)
    : IPayPalPayoutService
{
    public async Task<PayPalPayoutResult> SendPayoutAsync(
        string recipientEmail,
        decimal amount,
        string currency,
        string senderItemId,
        string note,
        CancellationToken ct)
    {
        string token  = await tokenCache.GetTokenAsync(ct);
        HttpClient client = httpClientFactory.CreateClient("PayPal");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        object body = new
        {
            sender_batch_header = new
            {
                sender_batch_id = senderItemId,
                email_subject   = "Pena e Artë — Session payout",
                email_message   = note,
            },
            items = new[]
            {
                new
                {
                    recipient_type = "EMAIL",
                    amount         = new { value = amount.ToString("F2"), currency },
                    receiver       = recipientEmail,
                    sender_item_id = senderItemId,
                    note           = note,
                },
            },
        };

        using HttpResponseMessage response = await client.PostAsync(
            "/v1/payments/payouts",
            new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"), ct);
        response.EnsureSuccessStatusCode();

        JsonDocument doc     = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        string batchId       = doc.RootElement
            .GetProperty("batch_header")
            .GetProperty("payout_batch_id")
            .GetString()!;
        string batchStatus   = doc.RootElement
            .GetProperty("batch_header")
            .GetProperty("batch_status")
            .GetString()!;

        return new PayPalPayoutResult(batchId, batchStatus);
    }

    public async Task<string> GetBatchStatusAsync(string batchId, CancellationToken ct)
    {
        string token  = await tokenCache.GetTokenAsync(ct);
        HttpClient client = httpClientFactory.CreateClient("PayPal");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await client.GetAsync(
            $"/v1/payments/payouts/{batchId}", ct);
        response.EnsureSuccessStatusCode();

        JsonDocument doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement
            .GetProperty("batch_header")
            .GetProperty("batch_status")
            .GetString()!;
    }
}
```

### 2.6 EF Core Configurations

**Create** `Pena_e_Arte.Infrastructure/Persistence/Configurations/StudioPayoutMethodConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class StudioPayoutMethodConfiguration : IEntityTypeConfiguration<StudioPayoutMethod>
{
    public void Configure(EntityTypeBuilder<StudioPayoutMethod> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.PayPalEmail).HasMaxLength(320);
        builder.Property(x => x.BankHolder).HasMaxLength(200);
        builder.Property(x => x.BankIban).HasMaxLength(34);
        builder.Property(x => x.BankSwift).HasMaxLength(11);

        // One per studio (tenant) — enforced by unique index on TenantId
        builder.HasIndex(x => x.TenantId).IsUnique();
    }
}
```

**Create** `Pena_e_Arte.Infrastructure/Persistence/Configurations/StudioPayoutConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pena_e_Arte.Domain.Entities;

namespace Pena_e_Arte.Infrastructure.Persistence.Configurations;

public class StudioPayoutConfiguration : IEntityTypeConfiguration<StudioPayout>
{
    public void Configure(EntityTypeBuilder<StudioPayout> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Currency).HasMaxLength(3);
        builder.Property(x => x.PayPalBatchId).HasMaxLength(50);
        builder.Property(x => x.PayPalPayoutItemId).HasMaxLength(50);
        builder.Property(x => x.FailureReason).HasMaxLength(500);

        builder.HasOne(x => x.Payment)
            .WithOne(p => p.Payout)
            .HasForeignKey<StudioPayout>(x => x.PaymentId);

        builder.HasIndex(x => x.PayPalBatchId);
    }
}
```

**Edit** `Pena_e_Arte.Infrastructure/Persistence/Configurations/PaymentConfiguration.cs`
— add new nullable columns (if the file already has a `PaymentConfiguration`, add these
properties; if it doesn't exist, create it):

```csharp
builder.Property(x => x.Method).HasConversion<string>().HasMaxLength(20);
builder.Property(x => x.PayPalOrderId).HasMaxLength(50);
builder.Property(x => x.PayPalCaptureId).HasMaxLength(50);
```

**Add DbSets to `AppDbContext`:**

```csharp
public DbSet<StudioPayoutMethod> StudioPayoutMethods { get; set; }
public DbSet<StudioPayout>       StudioPayouts       { get; set; }
```

**Generate and apply migration:**

```bash
dotnet ef migrations add AddPayPalPaymentAndPayoutEntities \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
dotnet ef database update \
    --project Pena_e_Arte.Infrastructure \
    --startup-project Pena_e_Arte.API
```

Review the generated migration before applying. Confirm it adds:
- `StudioPayoutMethods` table
- `StudioPayouts` table
- `Method`, `PayPalOrderId`, `PayPalCaptureId` columns on `Payments`

### 2.7 Register Services in DI

**Edit** the relevant `ServiceCollectionExtensions` (wherever Stripe services are registered):

```csharp
// PayPal options
builder.Services.Configure<PayPalOptions>(
    builder.Configuration.GetSection(PayPalOptions.SectionName));

// Singleton token cache (reused across requests)
builder.Services.AddSingleton<PayPalTokenCache>();

// Named HttpClient for PayPal
builder.Services.AddHttpClient("PayPal", (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<PayPalOptions>>().Value;
    client.BaseAddress = new Uri(opts.BaseUrl);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
    client.DefaultRequestHeaders.Add("Accept-Language", "en_US");
});

// PayPal services
builder.Services.AddScoped<IPayPalCheckoutService, PayPalCheckoutService>();
builder.Services.AddScoped<IPayPalPayoutService,   PayPalPayoutService>();

// Updated Stripe service (aggregator model — no connected account)
builder.Services.AddScoped<IStripePaymentService, StripePaymentService>();
```

---

## SECTION 3 — Application Layer

### 3.1 Update `CreatePaymentIntentCommand`

**Edit** `Pena_e_Arte.Application/Payments/Commands/CreatePaymentIntentCommand.cs`:

Remove any reference to `connectedAccountId` or `studio.StripeAccountId`. The handler
should call:

```csharp
(string intentId, string secret) = await _stripePaymentService.CreatePaymentIntentAsync(
    amountInCents: (long)(payment.Amount * 100),
    currency:      "EUR",
    paymentId:     payment.Id,
    ct);
```

No `StripeAccountId` lookup needed.

### 3.2 New Command: `CreatePayPalOrderCommand`

**Create** `Pena_e_Arte.Application/Payments/Commands/CreatePayPalOrderCommand.cs`:

```csharp
using MediatR;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Persistence;

namespace Pena_e_Arte.Application.Payments.Commands;

public record CreatePayPalOrderCommand(Guid AppointmentId) : IRequest<PayPalOrderResponse>;

public class CreatePayPalOrderHandler(
    AppDbContext         db,
    ICurrentTenant       tenant,
    IPayPalCheckoutService payPal)
    : IRequestHandler<CreatePayPalOrderCommand, PayPalOrderResponse>
{
    public async Task<PayPalOrderResponse> Handle(
        CreatePayPalOrderCommand command, CancellationToken ct)
    {
        Appointment appointment = await db.Appointments
            .FirstOrDefaultAsync(a => a.Id == command.AppointmentId, ct)
            ?? throw new AppointmentNotFoundException(command.AppointmentId);

        // Create a Payment record for tracking
        Payment payment = new()
        {
            AppointmentId = appointment.Id,
            ClientId      = appointment.ClientId,
            Amount        = appointment.DepositAmount,
            Status        = PaymentStatus.Pending,
            Method        = ClientPaymentMethod.PayPal,
            TenantId      = tenant.StudioId,
        };
        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        PayPalOrderResult order = await payPal.CreateOrderAsync(
            amount:    payment.Amount,
            currency:  "EUR",
            paymentId: payment.Id,
            ct);

        payment.PayPalOrderId = order.OrderId;
        await db.SaveChangesAsync(ct);

        return new PayPalOrderResponse(payment.Id, order.OrderId, order.ApproveUrl);
    }
}
```

**Create** `Pena_e_Arte.Application/Payments/Validators/CreatePayPalOrderValidator.cs`:

```csharp
using FluentValidation;

namespace Pena_e_Arte.Application.Payments.Validators;

public class CreatePayPalOrderValidator : AbstractValidator<CreatePayPalOrderCommand>
{
    public CreatePayPalOrderValidator()
    {
        RuleFor(x => x.AppointmentId).NotEmpty();
    }
}
```

### 3.3 New Command: `CapturePayPalOrderCommand`

**Create** `Pena_e_Arte.Application/Payments/Commands/CapturePayPalOrderCommand.cs`:

```csharp
using MediatR;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payments.Commands;

public record CapturePayPalOrderCommand(Guid PaymentId, string PayPalOrderId)
    : IRequest<PaymentResponse>;

public class CapturePayPalOrderHandler(AppDbContext db, IPayPalCheckoutService payPal)
    : IRequestHandler<CapturePayPalOrderCommand, PaymentResponse>
{
    public async Task<PaymentResponse> Handle(
        CapturePayPalOrderCommand command, CancellationToken ct)
    {
        Payment payment = await db.Payments
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, ct)
            ?? throw new PaymentNotFoundException(command.PaymentId);

        if (payment.PayPalOrderId != command.PayPalOrderId)
            throw new DomainException("PayPal order ID mismatch.");

        PayPalCaptureResult capture = await payPal.CaptureOrderAsync(command.PayPalOrderId, ct);

        payment.PayPalCaptureId = capture.CaptureId;
        payment.Status          = PaymentStatus.Captured;
        payment.PaidAt          = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return payment.ToResponse();
    }
}
```

**Create validator** `CapturePayPalOrderValidator`:

```csharp
public class CapturePayPalOrderValidator : AbstractValidator<CapturePayPalOrderCommand>
{
    public CapturePayPalOrderValidator()
    {
        RuleFor(x => x.PaymentId).NotEmpty();
        RuleFor(x => x.PayPalOrderId).NotEmpty();
    }
}
```

### 3.4 New Commands: Payout Method Management

**Create** `Pena_e_Arte.Application/Payouts/Commands/UpsertPayoutMethodCommand.cs`:

```csharp
using MediatR;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Enums;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Payouts.Commands;

public record UpsertPayoutMethodCommand(UpsertPayoutMethodRequest Request) : IRequest<PayoutMethodResponse>;

public class UpsertPayoutMethodHandler(AppDbContext db, ICurrentTenant tenant)
    : IRequestHandler<UpsertPayoutMethodCommand, PayoutMethodResponse>
{
    public async Task<PayoutMethodResponse> Handle(
        UpsertPayoutMethodCommand command, CancellationToken ct)
    {
        StudioPayoutMethod? existing = await db.StudioPayoutMethods
            .FirstOrDefaultAsync(ct);

        if (existing is null)
        {
            existing = new StudioPayoutMethod { TenantId = tenant.StudioId };
            db.StudioPayoutMethods.Add(existing);
        }

        existing.Type        = command.Request.Type;
        existing.PayPalEmail = command.Request.PayPalEmail;
        existing.BankHolder  = command.Request.BankHolder;
        existing.BankIban    = command.Request.BankIban;
        existing.BankSwift   = command.Request.BankSwift;
        existing.UpdatedAt   = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);
        return existing.ToResponse();
    }
}
```

**Create** `Pena_e_Arte.Application/Payouts/Validators/UpsertPayoutMethodValidator.cs`:

```csharp
public class UpsertPayoutMethodValidator : AbstractValidator<UpsertPayoutMethodCommand>
{
    public UpsertPayoutMethodValidator()
    {
        RuleFor(x => x.Request.Type).IsInEnum();

        When(x => x.Request.Type == PayoutMethodType.PayPal, () =>
        {
            RuleFor(x => x.Request.PayPalEmail)
                .NotEmpty().EmailAddress()
                .WithMessage("A valid PayPal email is required.");
        });

        When(x => x.Request.Type == PayoutMethodType.BankTransfer, () =>
        {
            RuleFor(x => x.Request.BankHolder).NotEmpty().MaximumLength(200);
            RuleFor(x => x.Request.BankIban).NotEmpty().MaximumLength(34);
        });
    }
}
```

**Create** `Pena_e_Arte.Application/Payouts/Commands/InitiatePayoutCommand.cs`:

```csharp
/// <summary>
/// Issuer or owner triggers a payout to the studio after a completed session.
/// Primary method: PayPal Payouts. Fallback: marks as ManualPending.
/// </summary>
public record InitiatePayoutCommand(Guid PaymentId) : IRequest<StudioPayoutResponse>;

public class InitiatePayoutHandler(
    AppDbContext          db,
    ICurrentTenant        tenant,
    IPayPalPayoutService  payPal)
    : IRequestHandler<InitiatePayoutCommand, StudioPayoutResponse>
{
    public async Task<StudioPayoutResponse> Handle(
        InitiatePayoutCommand command, CancellationToken ct)
    {
        Payment payment = await db.Payments
            .Include(p => p.Payout)
            .FirstOrDefaultAsync(p => p.Id == command.PaymentId, ct)
            ?? throw new PaymentNotFoundException(command.PaymentId);

        if (payment.Payout is not null)
            throw new DomainException("A payout for this payment already exists.");

        if (payment.Status != PaymentStatus.Captured)
            throw new DomainException("Cannot payout an uncaptured payment.");

        StudioPayoutMethod? method = await db.StudioPayoutMethods.FirstOrDefaultAsync(ct);

        StudioPayout payout = new()
        {
            PaymentId = payment.Id,
            Amount    = payment.Amount,
            Currency  = "EUR",
            Status    = PayoutStatus.Pending,
            Method    = method?.Type ?? PayoutMethodType.BankTransfer,
            TenantId  = tenant.StudioId,
        };
        db.StudioPayouts.Add(payout);
        await db.SaveChangesAsync(ct);

        if (method?.Type == PayoutMethodType.PayPal && !string.IsNullOrWhiteSpace(method.PayPalEmail))
        {
            payout.Status = PayoutStatus.Processing;
            await db.SaveChangesAsync(ct);

            PayPalPayoutResult result = await payPal.SendPayoutAsync(
                recipientEmail: method.PayPalEmail,
                amount:         payout.Amount,
                currency:       payout.Currency,
                senderItemId:   payout.Id.ToString(),
                note:           $"Pena e Artë — Payout for session {payment.AppointmentId}",
                ct);

            payout.PayPalBatchId  = result.BatchId;
            payout.ProcessedAt    = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return payout.ToResponse();
    }
}
```

---

## SECTION 4 — Contracts

**Create** `Pena_e_Arte.Contracts/Requests/CreatePayPalOrderRequest.cs`:

```csharp
public record CreatePayPalOrderRequest(Guid AppointmentId);
```

**Create** `Pena_e_Arte.Contracts/Requests/CapturePayPalOrderRequest.cs`:

```csharp
public record CapturePayPalOrderRequest(Guid PaymentId, string PayPalOrderId);
```

**Create** `Pena_e_Arte.Contracts/Requests/UpsertPayoutMethodRequest.cs`:

```csharp
using Pena_e_Arte.Domain.Enums;

public record UpsertPayoutMethodRequest(
    PayoutMethodType Type,
    string?          PayPalEmail,
    string?          BankHolder,
    string?          BankIban,
    string?          BankSwift);
```

**Create** `Pena_e_Arte.Contracts/Responses/PayPalOrderResponse.cs`:

```csharp
public record PayPalOrderResponse(Guid PaymentId, string PayPalOrderId, string ApproveUrl);
```

**Create** `Pena_e_Arte.Contracts/Responses/PayoutMethodResponse.cs`:

```csharp
using Pena_e_Arte.Domain.Enums;

public record PayoutMethodResponse(
    PayoutMethodType Type,
    string?          PayPalEmail,
    string?          BankHolder,
    string?          BankIban,
    bool             IsVerified,
    DateTime         UpdatedAt);
```

**Create** `Pena_e_Arte.Contracts/Responses/StudioPayoutResponse.cs`:

```csharp
using Pena_e_Arte.Domain.Enums;

public record StudioPayoutResponse(
    Guid         Id,
    Guid         PaymentId,
    decimal      Amount,
    string       Currency,
    PayoutStatus Status,
    PayoutMethodType Method,
    string?      PayPalBatchId,
    string?      FailureReason,
    DateTime     CreatedAt,
    DateTime?    CompletedAt);
```

---

## SECTION 5 — API Layer

### 5.1 Update `PaymentEndpoints.cs`

Add two new endpoints for PayPal checkout:

```csharp
group.MapPost("/paypal/order",
    CreatePayPalOrder).RequireAuthorization("ClientAndAbove");

group.MapPost("/paypal/capture",
    CapturePayPalOrder).RequireAuthorization("ClientAndAbove");

private static async Task<IResult> CreatePayPalOrder(
    CreatePayPalOrderRequest request,
    ISender                  mediator,
    CancellationToken        ct)
{
    PayPalOrderResponse result = await mediator.Send(
        new CreatePayPalOrderCommand(request.AppointmentId), ct);
    return Results.Created($"/api/v1/payments/{result.PaymentId}", result);
}

private static async Task<IResult> CapturePayPalOrder(
    CapturePayPalOrderRequest request,
    ISender                   mediator,
    CancellationToken         ct)
{
    PaymentResponse result = await mediator.Send(
        new CapturePayPalOrderCommand(request.PaymentId, request.PayPalOrderId), ct);
    return Results.Ok(result);
}
```

Also remove the `connectedAccountId`-related lookups from `CreatePaymentIntent` endpoint
if any remain there (they belong in the handler, not the endpoint, but verify).

### 5.2 Update `StudioEndpoints.cs`

Remove or stub out `ConnectStudio` endpoint since Stripe Connect is deprecated:

```csharp
// REMOVE:
// group.MapPost("/connect", ConnectStudio).RequireAuthorization("OwnerOnly");

// REPLACE WITH: informational endpoint telling the client Connect is unavailable
group.MapGet("/connect/status", GetConnectStatus).RequireAuthorization("OwnerOnly");

private static IResult GetConnectStatus()
{
    return Results.Ok(new
    {
        available = false,
        message   = "Stripe Connect is not available in your region. " +
                    "Studio payouts are processed via PayPal. " +
                    "Configure your payout method in Studio Settings.",
    });
}
```

### 5.3 New `PayoutEndpoints.cs`

**Create** `Pena_e_Arte.API/Endpoints/PayoutEndpoints.cs`:

```csharp
using MediatR;
using Pena_e_Arte.Application.Payouts.Commands;
using Pena_e_Arte.Application.Payouts.Queries;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;

namespace Pena_e_Arte.API.Endpoints;

public static class PayoutEndpoints
{
    public static void MapPayoutEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder group = app.MapGroup("/api/v1/payouts")
            .RequireAuthorization();

        // Studio owner: view and configure payout method
        group.MapGet("/method",   GetPayoutMethod).RequireAuthorization("OwnerOnly");
        group.MapPut("/method",   UpsertPayoutMethod).RequireAuthorization("OwnerOnly");

        // Studio owner: view payouts for their studio
        group.MapGet("/",         GetPayouts).RequireAuthorization("OwnerOnly");

        // Owner or issuer: initiate a payout for a captured payment
        group.MapPost("/",        InitiatePayout).RequireAuthorization("OwnerOnly");
    }

    private static async Task<IResult> GetPayoutMethod(ISender m, CancellationToken ct)
    {
        PayoutMethodResponse? result = await m.Send(new GetPayoutMethodQuery(), ct);
        return result is null ? Results.NoContent() : Results.Ok(result);
    }

    private static async Task<IResult> UpsertPayoutMethod(
        UpsertPayoutMethodRequest req, ISender m, CancellationToken ct)
    {
        PayoutMethodResponse result = await m.Send(new UpsertPayoutMethodCommand(req), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetPayouts(ISender m, CancellationToken ct)
    {
        IReadOnlyList<StudioPayoutResponse> result = await m.Send(new GetPayoutsQuery(), ct);
        return Results.Ok(result);
    }

    private static async Task<IResult> InitiatePayout(
        InitiatePayoutRequest req, ISender m, CancellationToken ct)
    {
        StudioPayoutResponse result = await m.Send(new InitiatePayoutCommand(req.PaymentId), ct);
        return Results.Created($"/api/v1/payouts/{result.Id}", result);
    }
}
```

Register `app.MapPayoutEndpoints()` in `Program.cs`.

---

## SECTION 6 — Frontend

### 6.1 Environment Setup

**Create/update** `frontend/.env.local` (gitignored — never commit):

```
VITE_STRIPE_PUBLISHABLE_KEY=pk_test_...   # your test key
VITE_PAYPAL_CLIENT_ID=...                 # your PayPal sandbox app client ID
```

**Create/update** `frontend/.env.example` (committed, no real values):

```
VITE_STRIPE_PUBLISHABLE_KEY=pk_test_replace_me
VITE_PAYPAL_CLIENT_ID=paypal_client_id_replace_me
```

### 6.2 PayPal Provider Setup

**Edit** `frontend/src/main.tsx` — wrap the app with `PayPalScriptProvider`:

```tsx
import { PayPalScriptProvider } from "@paypal/react-paypal-js";

<PayPalScriptProvider options={{
  clientId:  import.meta.env.VITE_PAYPAL_CLIENT_ID,
  currency:  "EUR",
  intent:    "capture",
}}>
  <App />
</PayPalScriptProvider>
```

### 6.3 New Types

**Edit** `frontend/src/features/payments/payment.types.ts` — add:

```typescript
export interface PayPalOrderResponse {
  paymentId:    string;
  payPalOrderId: string;
  approveUrl:   string;
}

export interface PayoutMethodResponse {
  type:          "PayPal" | "BankTransfer";
  payPalEmail:   string | null;
  bankHolder:    string | null;
  bankIban:      string | null;
  isVerified:    boolean;
  updatedAt:     string;
}

export interface StudioPayoutResponse {
  id:            string;
  paymentId:     string;
  amount:        number;
  currency:      string;
  status:        "Pending" | "Processing" | "Completed" | "Failed";
  method:        "PayPal" | "BankTransfer";
  payPalBatchId: string | null;
  failureReason: string | null;
  createdAt:     string;
  completedAt:   string | null;
}
```

### 6.4 Update `paymentsApi.ts`

Add PayPal endpoints and payout endpoints:

```typescript
// In paymentsApi endpoints:
createPayPalOrder: builder.mutation<PayPalOrderResponse, { appointmentId: string }>({
  query: (body) => ({ url: "payments/paypal/order", method: "POST", body }),
  invalidatesTags: ["Payment"],
}),
capturePayPalOrder: builder.mutation<PaymentResponse, { paymentId: string; payPalOrderId: string }>({
  query: (body) => ({ url: "payments/paypal/capture", method: "POST", body }),
  invalidatesTags: ["Payment"],
}),
getPayoutMethod: builder.query<PayoutMethodResponse | null, void>({
  query: () => "payouts/method",
  providesTags: ["PayoutMethod"],
}),
upsertPayoutMethod: builder.mutation<PayoutMethodResponse, UpsertPayoutMethodRequest>({
  query: (body) => ({ url: "payouts/method", method: "PUT", body }),
  invalidatesTags: ["PayoutMethod"],
}),
getPayouts: builder.query<StudioPayoutResponse[], void>({
  query: () => "payouts",
  providesTags: ["Payout"],
}),
initiatePayout: builder.mutation<StudioPayoutResponse, { paymentId: string }>({
  query: (body) => ({ url: "payouts", method: "POST", body }),
  invalidatesTags: ["Payout"],
}),
```

Add tag types: `"PayoutMethod"`, `"Payout"`.

### 6.5 New Component: `PaymentMethodSelector`

**Create** `frontend/src/features/payments/components/PaymentMethodSelector.tsx`:

This is the core UX component shown to clients at booking time. It lets them choose
between Stripe (card, Apple Pay, Google Pay) and PayPal.

```tsx
import { useState } from "react";
import { loadStripe } from "@stripe/stripe-js";
import { Elements, PaymentElement, useStripe, useElements } from "@stripe/react-stripe-js";
import { PayPalButtons, usePayPalScriptReducer } from "@paypal/react-paypal-js";
import { CreditCard, Wallet } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { cn } from "@/shared/utils/cn";
import {
  useGetPaymentClientSecretQuery,
  useCreatePayPalOrderMutation,
  useCapturePayPalOrderMutation,
} from "@/features/payments/paymentsApi";

const stripePromise = loadStripe(import.meta.env.VITE_STRIPE_PUBLISHABLE_KEY);

type PaymentTab = "card" | "paypal";

interface PaymentMethodSelectorProps {
  paymentId:    string;
  appointmentId: string;
  amount:       number;
  currency:     string;
  onSuccess:    () => void;
  onError:      (message: string) => void;
}

// ── Stripe sub-form ────────────────────────────────────────────────────────

function StripeCheckoutForm({ onSuccess, onError }: Pick<PaymentMethodSelectorProps, "onSuccess" | "onError">) {
  const stripe   = useStripe();
  const elements = useElements();
  const [submitting, setSubmitting] = useState(false);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!stripe || !elements) return;
    setSubmitting(true);
    const { error } = await stripe.confirmPayment({
      elements,
      confirmParams: { return_url: `${window.location.origin}/booking/success` },
      redirect: "if_required",
    });
    setSubmitting(false);
    if (error) onError(error.message ?? "Payment failed.");
    else        onSuccess();
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-4">
      <PaymentElement />
      <Button type="submit" disabled={submitting || !stripe} className="w-full">
        {submitting ? "Processing…" : "Pay with card"}
      </Button>
    </form>
  );
}

// ── PayPal sub-form ────────────────────────────────────────────────────────

function PayPalCheckoutForm({
  appointmentId,
  onSuccess,
  onError,
}: Pick<PaymentMethodSelectorProps, "appointmentId" | "onSuccess" | "onError">) {
  const [{ isPending }]           = usePayPalScriptReducer();
  const [createOrder]             = useCreatePayPalOrderMutation();
  const [captureOrder]            = useCapturePayPalOrderMutation();

  return (
    <div className="space-y-3">
      {isPending && <p className="text-sm text-muted-foreground text-center">Loading PayPal…</p>}
      <PayPalButtons
        style={{ layout: "vertical", shape: "rect", color: "blue" }}
        createOrder={async () => {
          const result = await createOrder({ appointmentId }).unwrap();
          return result.payPalOrderId;
        }}
        onApprove={async (data) => {
          try {
            const order = await createOrder({ appointmentId }).unwrap();
            await captureOrder({ paymentId: order.paymentId, payPalOrderId: data.orderID }).unwrap();
            onSuccess();
          } catch {
            onError("PayPal capture failed. Please try again.");
          }
        }}
        onError={() => onError("PayPal encountered an error. Please try another method.")}
      />
    </div>
  );
}

// ── Main component ─────────────────────────────────────────────────────────

export function PaymentMethodSelector({
  paymentId,
  appointmentId,
  amount,
  currency,
  onSuccess,
  onError,
}: PaymentMethodSelectorProps) {
  const [tab, setTab] = useState<PaymentTab>("card");
  const { data: secretData } = useGetPaymentClientSecretQuery(paymentId);

  const tabClass = (active: boolean) =>
    cn(
      "flex items-center gap-2 flex-1 justify-center py-2.5 text-sm font-medium rounded-md transition-colors",
      active
        ? "bg-background text-foreground shadow-sm"
        : "text-muted-foreground hover:text-foreground"
    );

  return (
    <div className="space-y-4">
      {/* Tab selector */}
      <div className="flex gap-1 rounded-lg bg-muted p-1">
        <button type="button" className={tabClass(tab === "card")} onClick={() => setTab("card")}>
          <CreditCard className="h-4 w-4" />
          Card / Apple Pay / Google Pay
        </button>
        <button type="button" className={tabClass(tab === "paypal")} onClick={() => setTab("paypal")}>
          <Wallet className="h-4 w-4" />
          PayPal
        </button>
      </div>

      {/* Stripe tab */}
      {tab === "card" && secretData?.clientSecret && (
        <Elements
          stripe={stripePromise}
          options={{
            clientSecret: secretData.clientSecret,
            appearance:   { theme: "stripe" },
          }}
        >
          <StripeCheckoutForm onSuccess={onSuccess} onError={onError} />
        </Elements>
      )}

      {/* PayPal tab */}
      {tab === "paypal" && (
        <PayPalCheckoutForm
          appointmentId={appointmentId}
          onSuccess={onSuccess}
          onError={onError}
        />
      )}

      <p className="text-xs text-muted-foreground text-center">
        {currency.toUpperCase()} {amount.toFixed(2)} — deposit for your session.
        Charged immediately, applied to your total at checkout.
      </p>
    </div>
  );
}
```

### 6.6 New Component: `PayoutMethodSettings`

**Create** `frontend/src/features/studios/components/PayoutMethodSettings.tsx`:

Owner sets up their payout preference (PayPal email or bank transfer details).

Full spec:
- Header: "Payout Method" with subtitle "How you receive payments after each session."
- Type selector: two radio-style cards — "PayPal" (recommended badge) and "Bank Transfer".
- If PayPal selected: single email input with label "Your PayPal email address".
- If Bank Transfer selected: three inputs — Account holder name, IBAN, SWIFT/BIC.
- "Save" button calls `useUpsertPayoutMethodMutation`.
- If current method is PayPal and `isVerified`, show green "Verified" badge.
- If no method set yet, show the form with PayPal pre-selected (it's simpler).
- Data source: `useGetPayoutMethodQuery()`.
- Named export: `PayoutMethodSettings`.

### 6.7 Update `StudioProfilePage` or Settings Page

Import and render `<PayoutMethodSettings />` inside the studio settings/profile page
in a dedicated "Payments" section, below billing info and above branding settings.

### 6.8 Update `CreatePaymentIntentPage`

Replace the existing `CreatePaymentIntentPage` (which assumed Stripe Connect) with the
new `PaymentMethodSelector` component. The page should:
1. Fetch the `paymentId` from the route param (or create the payment on mount via RTK Query).
2. Pass `paymentId`, `appointmentId`, `amount`, `currency` to `<PaymentMethodSelector />`.
3. On `onSuccess`, navigate to `/booking/success`.
4. On `onError`, show an inline error message.

---

## SECTION 7 — PayPal Webhook (Optional but Recommended)

Handle PayPal IPN/webhook for payout status updates.

**Add endpoint** to `Program.cs`:

```csharp
// In a new PayPalWebhookEndpoints.cs:
app.MapPost("/api/v1/webhooks/paypal", HandlePayPalWebhook).AllowAnonymous();
```

Add to `AllowAnonymous` exceptions table in `architecture.md`:

| `POST /api/v1/webhooks/paypal` | Called by PayPal servers | PayPal-Transmission-Sig HMAC header validated against webhook ID |

**Handler logic:**

```csharp
private static async Task<IResult> HandlePayPalWebhook(
    HttpRequest       httpRequest,
    ISender           mediator,
    IConfiguration    configuration,
    CancellationToken ct)
{
    // 1. Read body as string
    // 2. Validate PayPal-Transmission-Sig, PayPal-Transmission-Id,
    //    PayPal-Cert-Url, PayPal-Auth-Algo headers
    // 3. On PAYMENT.PAYOUTS-ITEM.SUCCEEDED event:
    //    Send UpdatePayoutStatusCommand(batchId, PayoutStatus.Completed)
    // 4. On PAYMENT.PAYOUTS-ITEM.FAILED event:
    //    Send UpdatePayoutStatusCommand(batchId, PayoutStatus.Failed, failureReason)
    // 5. Return 200 OK always (PayPal retries on non-200)
}
```

**Create** `UpdatePayoutStatusCommand` in Application layer:
```csharp
public record UpdatePayoutStatusCommand(
    string      BatchId,
    PayoutStatus NewStatus,
    string?     FailureReason = null) : IRequest<Unit>;
```

---

## SECTION 8 — Tests

### Backend

**Create** `tests/Pena_e_Arte.IntegrationTests/Application/PayPalPaymentIntegrationTests.cs`:

```
CreatePayPalOrder_ValidAppointment_ReturnsOrderIdAndApproveUrl
CapturePayPalOrder_ValidOrderId_UpdatesPaymentToCapured
CapturePayPalOrder_OrderIdMismatch_ThrowsDomainException
UpsertPayoutMethod_PayPal_ValidEmail_Saves
UpsertPayoutMethod_PayPal_InvalidEmail_ThrowsValidationException
UpsertPayoutMethod_BankTransfer_MissingIban_ThrowsValidationException
InitiatePayout_PayPalMethod_CallsPayPalAndSetsBatchId
InitiatePayout_NoPayout Method_DefaultsToBankTransferPending
InitiatePayout_AlreadyPaidOut_ThrowsDomainException
```

**Create** `tests/Pena_e_Arte.UnitTests/Services/StripePaymentServiceTests.cs`:

```
CreatePaymentIntent_NoConnectedAccount_SucceedsWithPlatformAccount
// Verify no RequestOptions.StripeAccount is passed
```

### Frontend

**Create** `frontend/src/features/payments/__tests__/PaymentMethodSelector.test.tsx`:

```typescript
// renders card tab by default
// switches to PayPal tab on click
// Stripe form renders when clientSecret available
// PayPal buttons render when tab is paypal
// calls onSuccess after PayPal capture
// calls onError on PayPal error
```

---

## SECTION 9 — Environment Variables Reference

Document these in `appsettings.Development.json` (gitignored) for the backend:

```json
{
  "Stripe": {
    "PublishableKey": "pk_test_...",
    "SecretKey":      "sk_test_...",
    "WebhookSecretBilling": "whsec_...",
    "WebhookSecretConnect": "whsec_..."
  },
  "PayPal": {
    "ClientId":     "YOUR_PAYPAL_SANDBOX_CLIENT_ID",
    "ClientSecret": "YOUR_PAYPAL_SANDBOX_CLIENT_SECRET",
    "BaseUrl":      "https://api-m.sandbox.paypal.com",
    "WebhookId":    "YOUR_PAYPAL_WEBHOOK_ID"
  }
}
```

For the frontend, in `.env.local` (gitignored):

```
VITE_STRIPE_PUBLISHABLE_KEY=pk_test_...
VITE_PAYPAL_CLIENT_ID=YOUR_PAYPAL_SANDBOX_CLIENT_ID
```

**To get PayPal sandbox credentials:**
1. Go to developer.paypal.com → Apps & Credentials → Sandbox.
2. Create a new app called "Pena e Arte".
3. Copy Client ID and Client Secret.
4. Enable "Payouts" feature on the app (requires PayPal approval for production).
5. Set up a webhook for `PAYMENT.PAYOUTS-ITEM.SUCCEEDED` and `PAYMENT.PAYOUTS-ITEM.FAILED`.
6. Copy the Webhook ID.

---

## Final Checklist

Before committing:

- [ ] `StripePaymentService` has zero `RequestOptions { StripeAccount = ... }` calls.
- [ ] `StripeConnectService` is marked `[Obsolete]` and `ConnectStudio` endpoint replaced.
- [ ] Migration generated and reviewed — `StudioPayoutMethods`, `StudioPayouts` tables exist, `Payments` has `Method`, `PayPalOrderId`, `PayPalCaptureId` columns.
- [ ] `PayPalOptions` is bound from config, not hardcoded.
- [ ] `VITE_PAYPAL_CLIENT_ID` and `VITE_STRIPE_PUBLISHABLE_KEY` read from env vars, never hardcoded in source.
- [ ] `.env.local` and `appsettings.Development.json` are in `.gitignore`.
- [ ] `PaymentMethodSelector` renders both tabs, Stripe Elements and PayPal Buttons.
- [ ] `PayoutMethodSettings` allows owner to set PayPal email or bank details.
- [ ] `PayoutEndpoints` registered in `Program.cs`.
- [ ] `@paypal/react-paypal-js` installed (`pnpm add @paypal/react-paypal-js`).
- [ ] `PayPalScriptProvider` wraps the app in `main.tsx`.
- [ ] All integration tests pass: `dotnet test`.
- [ ] All frontend tests pass: `pnpm test`.
- [ ] No PII in any log line.
- [ ] `architecture.md` Decisions Log and Stripe Connect section updated (already done in docs update pass).
