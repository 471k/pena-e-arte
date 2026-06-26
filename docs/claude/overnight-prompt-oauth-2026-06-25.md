# Overnight Prompt — Google & Apple OAuth Sign-In
**Date:** 2026-06-25  
**Scope:** Full-stack — Application · Infrastructure · API · Frontend  
**Constraint:** Zero new NuGet or npm packages.  
- Google/Apple JS SDKs loaded from CDN via `<script>` tags in `index.html` — no npm install.  
- ID token validation uses `System.IdentityModel.Tokens.Jwt` + `Microsoft.IdentityModel.Tokens` — already in the project for JWT generation.  
- JWKS fetching via `IHttpClientFactory` — already registered.  
- JWKS caching via `IDistributedCache` (Redis) — already registered.

---

## Goal

Add "Continue with Google" and "Continue with Apple" buttons to:
1. **`/login`** — existing users sign in without typing a password.
2. **`/register`** — new studio owners register and skip the password fields in step 2.

The flow is **frontend-first**: the browser handles the OAuth popup via the provider's own JS SDK (loaded from CDN), receives an **ID token** (a signed JWT), and sends it to our backend. The backend validates the token's signature against the provider's public JWKS, extracts the verified email, and issues a Pena e Artë JWT + refresh token — exactly as the existing `LoginCommand` does.

---

## Step 0 — Read these files first

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/conventions.md
docs/claude/architecture.md
Pena_e_Arte.Domain/Interfaces/IIdentityService.cs
Pena_e_Arte.Infrastructure/Services/IdentityService.cs
Pena_e_Arte.Infrastructure/Extensions/InfrastructureServiceExtensions.cs
Pena_e_Arte.Application/Auth/Commands/LoginCommand.cs
Pena_e_Arte.Application/Auth/Commands/RegisterUserCommand.cs
Pena_e_Arte.API/Endpoints/AuthEndpoints.cs
Pena_e_Arte.API/appsettings.json
frontend/src/features/auth/authApi.ts
frontend/src/features/auth/authSlice.ts
frontend/src/features/auth/components/LoginPage.tsx
frontend/src/features/studios/components/RegisterStudioPage.tsx
frontend/src/shared/utils/jwt.ts
frontend/index.html
```

---

## Step 1 — Configuration

### 1a. `Pena_e_Arte.API/appsettings.json`

Add alongside existing sections:

```json
"Google": {
  "ClientId": ""
},
"Apple": {
  "ClientId": ""
}
```

Both values are populated via environment variables only. The JSON stays blank.

- `Google:ClientId` — the OAuth 2.0 Client ID from Google Cloud Console (e.g. `123456789.apps.googleusercontent.com`). Used to validate the `aud` claim in Google ID tokens.
- `Apple:ClientId` — the Apple Services ID (e.g. `com.penaearte.web`). Used to validate the `aud` claim in Apple ID tokens.

### 1b. Options classes

Create `Pena_e_Arte.Infrastructure/Options/GoogleOptions.cs`:

```csharp
namespace Pena_e_Arte.Infrastructure.Options;

public class GoogleOptions
{
    public const string Section = "Google";
    public string ClientId { get; init; } = "";
}
```

Create `Pena_e_Arte.Infrastructure/Options/AppleOptions.cs`:

```csharp
namespace Pena_e_Arte.Infrastructure.Options;

public class AppleOptions
{
    public const string Section = "Apple";
    public string ClientId { get; init; } = "";
}
```

Register both in `AddInfrastructure`:

```csharp
services.Configure<GoogleOptions>(configuration.GetSection(GoogleOptions.Section));
services.Configure<AppleOptions>(configuration.GetSection(AppleOptions.Section));
```

### 1c. Frontend environment variables

Add to `frontend/.env.example` (create if it doesn't exist):

```
VITE_GOOGLE_CLIENT_ID=
VITE_APPLE_CLIENT_ID=
```

Both are set in actual env (`.env.local` for dev, CI secret for prod). The `VITE_APPLE_CLIENT_ID` must match the Apple Services ID registered in Apple Developer Console.

---

## Step 2 — Frontend CDN scripts

### `frontend/index.html`

Load both provider SDKs before the closing `</body>` tag, after the Vite entry script:

```html
<!-- Google Identity Services — OAuth popup & One Tap -->
<script src="https://accounts.google.com/gsi/client" async defer></script>

<!-- Apple Sign In JS SDK -->
<script
  type="text/javascript"
  src="https://appleid.cdn-apple.com/appleauth/static/jsapi/appleid/1/en_US/appleid.auth.js"
  async
  defer
></script>
```

---

## Step 3 — TypeScript type declarations

These tell TypeScript about the global objects injected by the CDN scripts. No packages — just `.d.ts` files.

### `frontend/src/types/google-identity.d.ts`

```typescript
// Minimal type declarations for Google Identity Services (accounts.google.com/gsi/client).
// Only the surface we actually call is typed here.

interface CredentialResponse {
  credential:        string;
  select_by:         string;
  client_id:         string;
}

interface PromptMomentNotification {
  isNotDisplayed():  boolean;
  isSkippedMoment(): boolean;
  isDismissedMoment(): boolean;
  getNotDisplayedReason(): string;
  getSkippedReason(): string;
  getDismissedReason(): string;
}

interface IdConfiguration {
  client_id:  string;
  callback:   (response: CredentialResponse) => void;
  auto_select?: boolean;
  cancel_on_tap_outside?: boolean;
}

interface GsiButtonConfiguration {
  type:  'standard' | 'icon';
  theme?: 'outline' | 'filled_blue' | 'filled_black';
  size?:  'large' | 'medium' | 'small';
  text?:  'signin_with' | 'signup_with' | 'continue_with' | 'signin';
  shape?: 'rectangular' | 'pill' | 'circle' | 'square';
  width?: number;
}

interface Google {
  accounts: {
    id: {
      initialize(config: IdConfiguration): void;
      prompt(callback?: (notification: PromptMomentNotification) => void): void;
      renderButton(element: HTMLElement, config: GsiButtonConfiguration): void;
      disableAutoSelect(): void;
      cancel(): void;
    };
  };
}

declare global {
  interface Window {
    google?: Google;
  }
}

export {};
```

### `frontend/src/types/apple-id.d.ts`

```typescript
// Minimal type declarations for Apple Sign In JS SDK.

interface AppleSignInAuthorizationCode {
  code:  string;
}

interface AppleSignInAuthorization {
  code:     string;
  id_token: string;
  state:    string;
}

interface AppleSignInUser {
  email?: string;
  name?: {
    firstName?: string;
    lastName?:  string;
  };
}

interface AppleSignInResponse {
  authorization: AppleSignInAuthorization;
  user?:         AppleSignInUser;
}

interface AppleIDAuthConfig {
  clientId:    string;
  scope:       string;
  redirectURI: string;
  state?:      string;
  usePopup?:   boolean;
}

interface AppleIDAuth {
  init(config: AppleIDAuthConfig): void;
  signIn(): Promise<AppleSignInResponse>;
}

declare global {
  interface Window {
    AppleID?: {
      auth: AppleIDAuth;
    };
  }
}

export {};
```

---

## Step 4 — Frontend hooks

### `frontend/src/shared/hooks/useGoogleSignIn.ts`

```typescript
/**
 * Returns a function that opens the Google One Tap / popup flow
 * and resolves with the Google ID token (credential) string.
 * Rejects if the SDK is not loaded or the user closes the popup.
 *
 * No npm packages — relies on window.google injected by the GSI CDN script.
 */
export function useGoogleSignIn(): () => Promise<string> {
  return () =>
    new Promise<string>((resolve, reject) => {
      if (!window.google?.accounts?.id) {
        reject(new Error("Google Sign-In SDK not loaded."));
        return;
      }

      window.google.accounts.id.initialize({
        client_id:             import.meta.env.VITE_GOOGLE_CLIENT_ID as string,
        callback:              ({ credential }) => resolve(credential),
        auto_select:           false,
        cancel_on_tap_outside: true,
      });

      window.google.accounts.id.prompt((notification) => {
        if (notification.isNotDisplayed() || notification.isSkippedMoment()) {
          reject(new Error("Google sign-in was dismissed or not displayed."));
        }
      });
    });
}
```

### `frontend/src/shared/hooks/useAppleSignIn.ts`

```typescript
/**
 * Returns a function that opens the Apple Sign In popup
 * and resolves with the Apple ID token string.
 * Rejects if the SDK is not loaded or the user cancels.
 *
 * No npm packages — relies on window.AppleID injected by the Apple CDN script.
 * Apple Sign In requires HTTPS even in development (use a proxy or ngrok).
 */
export function useAppleSignIn(): () => Promise<string> {
  return () =>
    new Promise<string>((resolve, reject) => {
      if (!window.AppleID?.auth) {
        reject(new Error("Apple Sign-In SDK not loaded."));
        return;
      }

      window.AppleID.auth.init({
        clientId:    import.meta.env.VITE_APPLE_CLIENT_ID as string,
        scope:       "name email",
        redirectURI: window.location.origin,
        usePopup:    true,
      });

      window.AppleID.auth
        .signIn()
        .then((response) => resolve(response.authorization.id_token))
        .catch(() => reject(new Error("Apple sign-in was cancelled or failed.")));
    });
}
```

---

## Step 5 — Backend: IOAuthTokenValidator

### `Pena_e_Arte.Domain/Interfaces/IOAuthTokenValidator.cs`

```csharp
namespace Pena_e_Arte.Domain.Interfaces;

/// <summary>
/// Claims extracted from a validated Google or Apple ID token.
/// The validation has already verified the JWT signature against the provider's JWKS.
/// </summary>
public record OAuthUserInfo(
    string  Email,
    string  ProviderUserId,
    string? FirstName);

public interface IOAuthTokenValidator
{
    /// <summary>
    /// Validates a Google ID token. Fetches and caches Google's JWKS.
    /// Throws <see cref="InvalidOperationException"/> if the token is invalid or expired.
    /// </summary>
    Task<OAuthUserInfo> ValidateGoogleTokenAsync(string idToken, CancellationToken ct);

    /// <summary>
    /// Validates an Apple ID token. Fetches and caches Apple's JWKS.
    /// Throws <see cref="InvalidOperationException"/> if the token is invalid or expired.
    /// </summary>
    Task<OAuthUserInfo> ValidateAppleTokenAsync(string idToken, CancellationToken ct);
}
```

### `Pena_e_Arte.Infrastructure/Services/OAuthTokenValidator.cs`

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Pena_e_Arte.Domain.Interfaces;
using Pena_e_Arte.Infrastructure.Options;

namespace Pena_e_Arte.Infrastructure.Services;

/// <summary>
/// Validates Google and Apple ID tokens without any third-party SDK.
/// Uses IHttpClientFactory to fetch JWKS and IDistributedCache (Redis) to cache them.
/// JwtSecurityTokenHandler is from System.IdentityModel.Tokens.Jwt — already in the project.
/// </summary>
public sealed class OAuthTokenValidator(
    IHttpClientFactory             httpFactory,
    IDistributedCache              cache,
    IOptions<GoogleOptions>        googleOpts,
    IOptions<AppleOptions>         appleOpts,
    ILogger<OAuthTokenValidator>   logger) : IOAuthTokenValidator
{
    private const string GoogleJwksUrl  = "https://www.googleapis.com/oauth2/v3/certs";
    private const string GoogleIssuer   = "https://accounts.google.com";
    private const string AppleJwksUrl   = "https://appleid.apple.com/auth/keys";
    private const string AppleIssuer    = "https://appleid.apple.com";

    private readonly string _googleAudience = googleOpts.Value.ClientId;
    private readonly string _appleAudience  = appleOpts.Value.ClientId;

    // ── Public API ───────────────────────────────────────────────────────────

    public async Task<OAuthUserInfo> ValidateGoogleTokenAsync(string idToken, CancellationToken ct)
    {
        JsonWebKeySet jwks = await GetJwksAsync("jwks_google", GoogleJwksUrl, ct);

        TokenValidationParameters parameters = new()
        {
            ValidIssuer         = GoogleIssuer,
            ValidAudience       = _googleAudience,
            IssuerSigningKeys   = jwks.Keys,
            ValidateIssuer      = true,
            ValidateAudience    = true,
            ValidateLifetime    = true,
            ClockSkew           = TimeSpan.FromMinutes(5),
        };

        JwtSecurityTokenHandler handler  = new();
        System.Security.Claims.ClaimsPrincipal principal;

        try
        {
            principal = handler.ValidateToken(idToken, parameters, out _);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Google ID token validation failed");
            throw new InvalidOperationException("Invalid Google ID token.", ex);
        }

        string email  = principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                     ?? principal.FindFirst("email")?.Value
                     ?? throw new InvalidOperationException("Google token missing email claim.");

        string sub    = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? principal.FindFirst("sub")?.Value
                     ?? throw new InvalidOperationException("Google token missing sub claim.");

        string? given = principal.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value
                     ?? principal.FindFirst("given_name")?.Value;

        return new OAuthUserInfo(email.ToLowerInvariant(), sub, given);
    }

    public async Task<OAuthUserInfo> ValidateAppleTokenAsync(string idToken, CancellationToken ct)
    {
        JsonWebKeySet jwks = await GetJwksAsync("jwks_apple", AppleJwksUrl, ct);

        TokenValidationParameters parameters = new()
        {
            ValidIssuer         = AppleIssuer,
            ValidAudience       = _appleAudience,
            IssuerSigningKeys   = jwks.Keys,
            ValidateIssuer      = true,
            ValidateAudience    = true,
            ValidateLifetime    = true,
            ClockSkew           = TimeSpan.FromMinutes(5),
        };

        JwtSecurityTokenHandler handler = new();
        System.Security.Claims.ClaimsPrincipal principal;

        try
        {
            principal = handler.ValidateToken(idToken, parameters, out _);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Apple ID token validation failed");
            throw new InvalidOperationException("Invalid Apple ID token.", ex);
        }

        string email  = principal.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                     ?? principal.FindFirst("email")?.Value
                     ?? throw new InvalidOperationException("Apple token missing email claim.");

        string sub    = principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                     ?? principal.FindFirst("sub")?.Value
                     ?? throw new InvalidOperationException("Apple token missing sub claim.");

        // Apple only returns the name on the FIRST sign-in; subsequent sign-ins omit it.
        string? given = principal.FindFirst(System.Security.Claims.ClaimTypes.GivenName)?.Value
                     ?? principal.FindFirst("given_name")?.Value;

        return new OAuthUserInfo(email.ToLowerInvariant(), sub, given);
    }

    // ── JWKS helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fetches the JWKS from the provider and caches it in Redis for 1 hour.
    /// Google/Apple rotate keys infrequently; 1h is safe and avoids hammering the endpoint.
    /// </summary>
    private async Task<JsonWebKeySet> GetJwksAsync(
        string cacheKey, string url, CancellationToken ct)
    {
        byte[]? cached = await cache.GetAsync(cacheKey, ct);

        if (cached is not null)
        {
            string json = Encoding.UTF8.GetString(cached);
            return new JsonWebKeySet(json);
        }

        using HttpClient client = httpFactory.CreateClient("OAuthJwks");
        HttpResponseMessage response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        string jwksJson = await response.Content.ReadAsStringAsync(ct);

        await cache.SetAsync(
            cacheKey,
            Encoding.UTF8.GetBytes(jwksJson),
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
            },
            ct);

        logger.LogInformation("Fetched and cached JWKS from {Url}", url);
        return new JsonWebKeySet(jwksJson);
    }
}
```

Register in `AddInfrastructure`:

```csharp
services.AddHttpClient("OAuthJwks");
services.AddScoped<IOAuthTokenValidator, OAuthTokenValidator>();
```

---

## Step 6 — Backend: extend IIdentityService

### `Pena_e_Arte.Domain/Interfaces/IIdentityService.cs`

Add two new methods to the existing interface:

```csharp
/// <summary>
/// Issues a JWT for a user identified only by their verified email address.
/// The caller is responsible for having already validated the OAuth ID token.
/// Returns an error if no account exists with that email.
/// </summary>
Task<(bool Success, string? AccessToken, string? Error)> LoginWithVerifiedEmailAsync(string email);

/// <summary>
/// Creates an Identity user without a password (OAuth-only account).
/// The caller must have already verified the user's email via an OAuth ID token.
/// </summary>
Task<(bool Success, Guid UserId, string[] Errors)> CreateOAuthUserAsync(
    string email, string role, Guid studioId, string? firstName);
```

### `Pena_e_Arte.Infrastructure/Services/IdentityService.cs`

Add the two new method implementations to the existing `IdentityService` class:

```csharp
public async Task<(bool Success, string? AccessToken, string? Error)> LoginWithVerifiedEmailAsync(
    string email)
{
    IdentityUser? user = await userManager.FindByEmailAsync(email);

    if (user is null)
        return (false, null, "No account found with this email. Please register first.");

    IList<string> roles      = await userManager.GetRolesAsync(user);
    IList<Claim>  userClaims = await userManager.GetClaimsAsync(user);

    return (true, GenerateJwt(user, roles, userClaims), null);
}

public async Task<(bool Success, Guid UserId, string[] Errors)> CreateOAuthUserAsync(
    string email, string role, Guid studioId, string? firstName)
{
    // Create the Identity user with no password — they will always sign in via OAuth.
    IdentityUser user = new() { UserName = email, Email = email, EmailConfirmed = true };
    IdentityResult result = await userManager.CreateAsync(user);

    if (!result.Succeeded)
        return (false, Guid.Empty, result.Errors.Select(e => e.Description).ToArray());

    await userManager.AddToRoleAsync(user, role);
    await userManager.AddClaimAsync(user, new Claim("tenant_id", studioId.ToString()));
    if (firstName is not null)
        await userManager.AddClaimAsync(user, new Claim(JwtRegisteredClaimNames.GivenName, firstName));

    return (true, Guid.Parse(user.Id), []);
}
```

---

## Step 7 — Contracts

### `Pena_e_Arte.Contracts/Requests/OAuthLoginRequest.cs`

```csharp
namespace Pena_e_Arte.Contracts.Requests;

/// <summary>
/// Sent by the frontend after receiving a Google or Apple ID token.
/// The provider field is "google" or "apple" (lowercase).
/// </summary>
public record OAuthLoginRequest(string Provider, string IdToken);
```

### `Pena_e_Arte.Contracts/Requests/RegisterOAuthUserRequest.cs`

```csharp
namespace Pena_e_Arte.Contracts.Requests;

/// <summary>
/// Sent by the frontend during studio registration when the owner chose OAuth
/// instead of email/password. The role is always "owner" for studio registration.
/// </summary>
public record RegisterOAuthUserRequest(
    string Provider,
    string IdToken,
    string Role,
    Guid   StudioId);
```

---

## Step 8 — Application: Commands

### `Pena_e_Arte.Application/Auth/Commands/OAuthLoginCommand.cs`

```csharp
using MediatR;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Contracts.Responses;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record OAuthLoginCommand(OAuthLoginRequest Request) : IRequest<AuthResponse>;

public class OAuthLoginHandler(
    IOAuthTokenValidator validator,
    IIdentityService     identity) : IRequestHandler<OAuthLoginCommand, AuthResponse>
{
    public async Task<AuthResponse> Handle(OAuthLoginCommand command, CancellationToken ct)
    {
        OAuthUserInfo info = command.Request.Provider switch
        {
            "google" => await validator.ValidateGoogleTokenAsync(command.Request.IdToken, ct),
            "apple"  => await validator.ValidateAppleTokenAsync(command.Request.IdToken, ct),
            _        => throw new BusinessRuleViolationException(
                             $"Unsupported OAuth provider: {command.Request.Provider}"),
        };

        (bool success, string? accessToken, string? error) =
            await identity.LoginWithVerifiedEmailAsync(info.Email);

        if (!success)
            throw new BusinessRuleViolationException(
                error ?? "No account found. Please register first.");

        string refreshToken = await identity.CreateRefreshTokenAsync(info.Email);
        return new AuthResponse(accessToken!, refreshToken);
    }
}
```

### `Pena_e_Arte.Application/Auth/Commands/RegisterOAuthUserCommand.cs`

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pena_e_Arte.Application.Persistence;
using Pena_e_Arte.Contracts.Requests;
using Pena_e_Arte.Domain.Entities;
using Pena_e_Arte.Domain.Exceptions;
using Pena_e_Arte.Domain.Interfaces;

namespace Pena_e_Arte.Application.Auth.Commands;

public record RegisterOAuthUserCommand(RegisterOAuthUserRequest Request) : IRequest;

public class RegisterOAuthUserHandler(
    IOAuthTokenValidator validator,
    IIdentityService     identity,
    IAppDbContext        db) : IRequestHandler<RegisterOAuthUserCommand>
{
    public async Task Handle(RegisterOAuthUserCommand command, CancellationToken ct)
    {
        RegisterOAuthUserRequest req = command.Request;

        OAuthUserInfo info = req.Provider switch
        {
            "google" => await validator.ValidateGoogleTokenAsync(req.IdToken, ct),
            "apple"  => await validator.ValidateAppleTokenAsync(req.IdToken, ct),
            _        => throw new BusinessRuleViolationException(
                             $"Unsupported OAuth provider: {req.Provider}"),
        };

        (bool success, Guid userId, string[] errors) =
            await identity.CreateOAuthUserAsync(
                info.Email, req.Role, req.StudioId, info.FirstName);

        if (!success)
            throw new BusinessRuleViolationException(string.Join("; ", errors));

        // Mirror the same Client-record logic as RegisterUserCommand for "client" role.
        // IgnoreQueryFilters required: registration is anonymous, no tenant JWT.
        if (req.Role == "client")
        {
            Client? existing = await db.Clients
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    c => c.StudioId == req.StudioId && c.Email == info.Email && c.UserId == null, ct);

            if (existing is not null)
            {
                existing.UserId    = userId;
                existing.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                db.Clients.Add(new Client
                {
                    StudioId  = req.StudioId,
                    UserId    = userId,
                    FirstName = info.FirstName ?? info.Email.Split('@')[0],
                    LastName  = string.Empty,
                    Email     = info.Email,
                });
            }

            await db.SaveChangesAsync(ct);
        }
    }
}
```

---

## Step 9 — FluentValidation

### `Pena_e_Arte.Application/Auth/Validators/OAuthLoginValidator.cs`

```csharp
using FluentValidation;
using Pena_e_Arte.Application.Auth.Commands;

namespace Pena_e_Arte.Application.Auth.Validators;

public class OAuthLoginValidator : AbstractValidator<OAuthLoginCommand>
{
    private static readonly string[] AllowedProviders = ["google", "apple"];

    public OAuthLoginValidator()
    {
        RuleFor(x => x.Request.Provider)
            .NotEmpty()
            .Must(p => AllowedProviders.Contains(p))
            .WithMessage("Provider must be 'google' or 'apple'.");

        RuleFor(x => x.Request.IdToken)
            .NotEmpty()
            .MaximumLength(4096);
    }
}
```

### `Pena_e_Arte.Application/Auth/Validators/RegisterOAuthUserValidator.cs`

```csharp
using FluentValidation;
using Pena_e_Arte.Application.Auth.Commands;

namespace Pena_e_Arte.Application.Auth.Validators;

public class RegisterOAuthUserValidator : AbstractValidator<RegisterOAuthUserCommand>
{
    private static readonly string[] AllowedProviders = ["google", "apple"];
    private static readonly string[] AllowedRoles     = ["owner", "client", "artist"];

    public RegisterOAuthUserValidator()
    {
        RuleFor(x => x.Request.Provider)
            .NotEmpty()
            .Must(p => AllowedProviders.Contains(p))
            .WithMessage("Provider must be 'google' or 'apple'.");

        RuleFor(x => x.Request.IdToken)
            .NotEmpty()
            .MaximumLength(4096);

        RuleFor(x => x.Request.Role)
            .NotEmpty()
            .Must(r => AllowedRoles.Contains(r))
            .WithMessage("Role must be 'owner', 'client', or 'artist'.");

        RuleFor(x => x.Request.StudioId)
            .NotEmpty();
    }
}
```

---

## Step 10 — API Endpoints

### `Pena_e_Arte.API/Endpoints/AuthEndpoints.cs`

Add two new route registrations inside the existing `MapAuthEndpoints` method:

```csharp
group.MapPost("/oauth/login",    OAuthLogin).AllowAnonymous();
group.MapPost("/oauth/register", OAuthRegister).AllowAnonymous();
```

Add the two handler methods to the `AuthEndpoints` class:

```csharp
private static async Task<IResult> OAuthLogin(
    OAuthLoginRequest request,
    ISender           mediator,
    CancellationToken ct)
{
    AuthResponse response = await mediator.Send(new OAuthLoginCommand(request), ct);
    return Results.Ok(response);
}

private static async Task<IResult> OAuthRegister(
    RegisterOAuthUserRequest request,
    ISender                  mediator,
    CancellationToken        ct)
{
    await mediator.Send(new RegisterOAuthUserCommand(request), ct);
    return Results.NoContent();
}
```

Add the required using:
```csharp
using Pena_e_Arte.Application.Auth.Commands;
```

---

## Step 11 — Frontend: authApi.ts

Add two new mutations to the existing `authApi`:

```typescript
oauthLogin: builder.mutation<AuthResponse, { provider: string; idToken: string }>({
  query: (body) => ({ url: "auth/oauth/login", method: "POST", body }),
}),

oauthRegister: builder.mutation<void, { provider: string; idToken: string; role: string; studioId: string }>({
  query: (body) => ({ url: "auth/oauth/register", method: "POST", body }),
}),
```

Export the hooks:

```typescript
export const {
  useLoginMutation,
  useRegisterUserMutation,
  useRequestPasswordResetMutation,
  useResetPasswordMutation,
  useOauthLoginMutation,
  useOauthRegisterMutation,
} = authApi;
```

---

## Step 12 — Frontend: OAuthButtons shared component

Create `frontend/src/shared/components/OAuthButtons.tsx`.

This component renders the "Continue with Google" and "Continue with Apple" buttons. It accepts an `onToken` callback that receives `{ provider, idToken }` and handles the async sign-in.

```tsx
import { useState } from "react";
import { Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { useGoogleSignIn } from "@/shared/hooks/useGoogleSignIn";
import { useAppleSignIn }  from "@/shared/hooks/useAppleSignIn";

interface OAuthButtonsProps {
  onToken: (result: { provider: "google" | "apple"; idToken: string }) => Promise<void>;
  disabled?: boolean;
}

export function OAuthButtons({ onToken, disabled = false }: OAuthButtonsProps) {
  const [loadingProvider, setLoadingProvider] = useState<"google" | "apple" | null>(null);
  const [error, setError] = useState<string | null>(null);

  const signInWithGoogle = useGoogleSignIn();
  const signInWithApple  = useAppleSignIn();

  async function handle(provider: "google" | "apple") {
    setError(null);
    setLoadingProvider(provider);
    try {
      const idToken = provider === "google"
        ? await signInWithGoogle()
        : await signInWithApple();
      await onToken({ provider, idToken });
    } catch (err) {
      const msg = err instanceof Error ? err.message : "Sign-in failed. Please try again.";
      // User-cancelled prompts produce an error we don't surface as an error to the user.
      if (!msg.toLowerCase().includes("dismiss") && !msg.toLowerCase().includes("cancel")) {
        setError(msg);
      }
    } finally {
      setLoadingProvider(null);
    }
  }

  const isLoading = loadingProvider !== null;

  return (
    <div className="space-y-3">
      {/* Divider */}
      <div className="relative">
        <div className="absolute inset-0 flex items-center">
          <span className="w-full border-t border-border/50" />
        </div>
        <div className="relative flex justify-center text-xs">
          <span className="bg-card px-2 text-foreground/40">or continue with</span>
        </div>
      </div>

      {/* Google */}
      <Button
        type="button"
        variant="outline"
        className="w-full gap-2"
        disabled={disabled || isLoading}
        onClick={() => handle("google")}
        aria-label="Continue with Google"
      >
        {loadingProvider === "google"
          ? <Loader2 className="h-4 w-4 animate-spin" />
          : (
            <svg role="img" aria-hidden="true" viewBox="0 0 24 24" className="h-4 w-4" fill="currentColor">
              <path d="M12.48 10.92v3.28h7.84c-.24 1.84-.853 3.187-1.787 4.133-1.147 1.147-2.933 2.4-6.053 2.4-4.827 0-8.6-3.893-8.6-8.72s3.773-8.72 8.6-8.72c2.6 0 4.507 1.027 5.907 2.347l2.307-2.307C18.747 1.44 16.133 0 12.48 0 5.867 0 .307 5.387.307 12s5.56 12 12.173 12c3.573 0 6.267-1.173 8.373-3.36 2.16-2.16 2.84-5.213 2.84-7.667 0-.76-.053-1.467-.173-2.053H12.48z"/>
            </svg>
          )
        }
        Continue with Google
      </Button>

      {/* Apple */}
      <Button
        type="button"
        variant="outline"
        className="w-full gap-2"
        disabled={disabled || isLoading}
        onClick={() => handle("apple")}
        aria-label="Continue with Apple"
      >
        {loadingProvider === "apple"
          ? <Loader2 className="h-4 w-4 animate-spin" />
          : (
            <svg role="img" aria-hidden="true" viewBox="0 0 24 24" className="h-4 w-4" fill="currentColor">
              <path d="M12.152 6.896c-.948 0-2.415-1.078-3.96-1.04-2.04.027-3.91 1.183-4.961 3.014-2.117 3.675-.546 9.103 1.519 12.09 1.013 1.454 2.208 3.09 3.792 3.039 1.52-.065 2.09-.987 3.935-.987 1.831 0 2.35.987 3.96.948 1.637-.026 2.676-1.48 3.676-2.948 1.156-1.688 1.636-3.325 1.662-3.415-.039-.013-3.182-1.221-3.22-4.857-.026-3.04 2.48-4.494 2.597-4.559-1.429-2.09-3.623-2.324-4.39-2.376-2-.156-3.675 1.09-4.61 1.09zM15.53 3.83c.843-1.012 1.4-2.427 1.245-3.83-1.207.052-2.662.805-3.532 1.818-.78.896-1.454 2.338-1.273 3.714 1.338.104 2.715-.688 3.559-1.701"/>
            </svg>
          )
        }
        Continue with Apple
      </Button>

      {error && (
        <p className="text-xs text-destructive text-center" role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
```

---

## Step 13 — Frontend: LoginPage.tsx

Open `frontend/src/features/auth/components/LoginPage.tsx`.

**Add the import:**
```tsx
import { OAuthButtons } from "@/shared/components/OAuthButtons";
import { useOauthLoginMutation } from "../authApi";
```

**Add the mutation hook** inside `LoginPage()`:
```tsx
const [oauthLogin] = useOauthLoginMutation();
```

**Add the `handleOAuthToken` callback** inside `LoginPage()`:
```tsx
async function handleOAuthToken({
  provider,
  idToken,
}: {
  provider: "google" | "apple";
  idToken: string;
}) {
  const { accessToken } = await oauthLogin({ provider, idToken }).unwrap();
  const payload = decodeToken(accessToken);
  dispatch(setCredentials(payload));
  const redirect = searchParams.get("redirect");
  navigate(redirect ?? getRoleRedirectPath(payload.role), { replace: true });
}
```

**Place `<OAuthButtons>` inside the Card**, after the closing `</form>` tag and before the registration prompt `<div>`:

```tsx
<OAuthButtons onToken={handleOAuthToken} disabled={isLoading} />
```

The final structure of `CardContent` should be:

```
<form>...</form>
<OAuthButtons onToken={handleOAuthToken} disabled={isLoading} />
<div> Don't have an account? ... </div>
```

---

## Step 14 — Frontend: RegisterStudioPage.tsx

This is more involved. Step 2 currently shows email, password, and confirmPassword. We need to add an OAuth path that pre-fills the email (read-only) and hides the password fields.

### 14a. State additions

Inside `RegisterStudioPage()`, add:

```tsx
const [oauthProvider, setOauthProvider] = useState<"google" | "apple" | null>(null);
const [oauthIdToken,  setOauthIdToken]  = useState<string | null>(null);

const [oauthRegister] = useOauthRegisterMutation();
```

Add import:
```tsx
import { useOauthRegisterMutation } from "@/features/auth/authApi";
import { OAuthButtons } from "@/shared/components/OAuthButtons";
```

### 14b. Schema update

The `schema` uses `.refine` to require passwords to match. When using OAuth, passwords are not needed. Update the schema to make passwords optional when OAuth is active. The cleanest approach: keep the Zod schema as-is, but skip password validation on submit if `oauthProvider !== null`.

Alternatively, use `.superRefine` to conditionally require passwords. But since this adds complexity to the schema, the simpler path is: if `oauthProvider !== null`, skip calling `registerUser` and instead call `oauthRegister`.

### 14c. Handle OAuth token receipt in step 2

Add a callback that the `OAuthButtons` component will call when the user completes the OAuth flow:

```tsx
async function handleOAuthToken({
  provider,
  idToken,
}: {
  provider: "google" | "apple";
  idToken: string;
}) {
  // Decode the provider ID token to extract the email (we trust it — the backend
  // will re-validate the signature; we just want to pre-fill the email field).
  try {
    // The ID token is a JWT — we can decode it client-side without verifying
    // the signature (we're just reading the payload for UX purposes).
    const parts  = idToken.split(".");
    if (parts.length !== 3) throw new Error("Malformed token");
    const claims = JSON.parse(atob(parts[1].replace(/-/g, "+").replace(/_/g, "/")));
    const email  = (claims.email as string | undefined) ?? "";

    setValue("email", email);
    setValue("password", "");          // clear passwords — not needed for OAuth
    setValue("confirmPassword", "");
  } catch {
    // If we can't decode the token client-side, we still proceed.
    // The backend will extract the email from the validated token.
  }

  setOauthProvider(provider);
  setOauthIdToken(idToken);
}
```

### 14d. Update `onSubmit`

In the `onSubmit` function, add a branch for OAuth registration:

```tsx
async function onSubmit(values: FormValues) {
  setServerError(null);
  try {
    const studio = await registerStudio({
      name:       values.name,
      slug:       values.slug,
      city:       values.city,
      latitude:   values.latitude,
      longitude:  values.longitude,
      ownerEmail: values.email,
      ...(pendingReferralCode ? { referralCode: pendingReferralCode } : {}),
    }).unwrap();

    if (oauthProvider && oauthIdToken) {
      // OAuth path: register without password, then log in
      await oauthRegister({
        provider:  oauthProvider,
        idToken:   oauthIdToken,
        role:      "owner",
        studioId:  studio.id,
      }).unwrap();

      // Log in using the same ID token
      const { accessToken } = await oauthLogin({
        provider: oauthProvider,
        idToken:  oauthIdToken,
      }).unwrap();

      dispatch(setCredentials(decodeToken(accessToken)));
    } else {
      // Password path (existing logic)
      await registerUser({
        email:    values.email,
        password: values.password,
        role:     "owner",
        studioId: studio.id,
      }).unwrap();

      const { accessToken } = await login({
        email:    values.email,
        password: values.password,
      }).unwrap();

      dispatch(setCredentials(decodeToken(accessToken)));
    }

    dispatch(setPendingReferralCode(null));
    navigate("/dashboard", { replace: true });
  } catch (err) {
    const message =
      typeof err === "object" && err !== null && "data" in err
        ? ((err as { data: { message?: string; detail?: string } }).data?.message ??
          (err as { data: { message?: string; detail?: string } }).data?.detail ??
          "Registration failed. Please try again.")
        : "Unable to reach the server. Please try again.";
    setServerError(message);
  }
}
```

Also add `useOauthLoginMutation` import and hook:
```tsx
const [oauthLogin] = useOauthLoginMutation();
```

### 14e. Step 2 UI

In the JSX for step 2 (the owner account section), make these changes:

1. When `oauthProvider !== null`, show the email field as read-only and hide the password fields. Add a "Change sign-in method" link that clears `oauthProvider` / `oauthIdToken`.

2. When `oauthProvider === null`, show the existing email + password + confirmPassword fields, then render `<OAuthButtons>` below.

The JSX for step 2 becomes:

```tsx
{step === 2 && (
  <>
    {/* Email — read-only when OAuth is active */}
    <div className="space-y-1.5">
      <Label htmlFor="email">Email</Label>
      <Input
        id="email"
        type="email"
        autoComplete="email"
        readOnly={oauthProvider !== null}
        className={oauthProvider !== null ? "bg-muted/40 cursor-default" : ""}
        {...register("email")}
      />
      {errors.email && (
        <p className="text-xs text-destructive">{errors.email.message}</p>
      )}
    </div>

    {/* Password fields — hidden when OAuth is active */}
    {oauthProvider === null && (
      <>
        <div className="space-y-1.5">
          <Label htmlFor="password">Password</Label>
          <PasswordInput
            id="password"
            autoComplete="new-password"
            placeholder="Min. 8 characters"
            {...register("password")}
          />
          {errors.password && (
            <p className="text-xs text-destructive">{errors.password.message}</p>
          )}
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="confirmPassword">Confirm password</Label>
          <PasswordInput
            id="confirmPassword"
            autoComplete="new-password"
            {...register("confirmPassword")}
          />
          {errors.confirmPassword && (
            <p className="text-xs text-destructive">{errors.confirmPassword.message}</p>
          )}
        </div>
      </>
    )}

    {/* OAuth confirmation pill — shown when a provider is connected */}
    {oauthProvider !== null && (
      <div className="flex items-center justify-between rounded-md border border-border/50 bg-muted/30 px-3 py-2">
        <p className="text-xs text-muted-foreground capitalize">
          Signing in with {oauthProvider}
        </p>
        <button
          type="button"
          onClick={() => { setOauthProvider(null); setOauthIdToken(null); }}
          className="text-xs underline underline-offset-2 hover:text-foreground text-muted-foreground"
        >
          Change
        </button>
      </div>
    )}

    {/* OAuth buttons — only shown when no provider is connected yet */}
    {oauthProvider === null && (
      <OAuthButtons onToken={handleOAuthToken} disabled={isSubmitting} />
    )}

    {serverError && (
      <p className="text-sm text-destructive text-center" role="alert">
        {serverError}
      </p>
    )}

    <Button type="submit" className="w-full" disabled={isSubmitting}>
      {isSubmitting ? <Loader2 className="h-4 w-4 animate-spin mr-2" /> : null}
      Create studio
    </Button>
  </>
)}
```

**Important:** The `PasswordInput` component must be imported in `RegisterStudioPage.tsx` if not already:
```tsx
import { PasswordInput } from "@/shared/components/ui/password-input";
```

### 14f. Schema refinement for OAuth path

The existing `schema.refine` requires password === confirmPassword. When on the OAuth path, passwords are empty strings — this would fail the refine. Fix by making the refine conditional:

```tsx
.superRefine((data, ctx) => {
  // Password match is only required when not using OAuth.
  // The form page tracks oauthProvider in local state; we pass it as a ref.
  // Simplest approach: always validate — but the OAuth path sets both fields to "".
  // "" === "" so the existing refine still passes. No change needed.
});
```

Actually: empty string === empty string, so the existing `.refine` passes. The `password` field has `.min(8)` which would fail for empty. Fix the schema to allow empty password when OAuth is active. The cleanest approach: change the schema `password` to `z.string()` (no min) and add a custom superRefine:

```tsx
password:        z.string(),
confirmPassword: z.string(),
```

And add a `superRefine` after the existing object:

```tsx
.superRefine((data, ctx) => {
  // If both password fields are empty, the user is on the OAuth path — skip password validation.
  if (data.password === "" && data.confirmPassword === "") return;
  
  if (data.password.length < 8) {
    ctx.addIssue({
      code: z.ZodIssueCode.too_small,
      minimum: 8,
      type: "string",
      inclusive: true,
      message: "Password must be at least 8 characters",
      path: ["password"],
    });
  }
  
  if (data.password !== data.confirmPassword) {
    ctx.addIssue({
      code: z.ZodIssueCode.custom,
      message: "Passwords do not match",
      path: ["confirmPassword"],
    });
  }
});
```

Remove the old `.refine(...)` call and replace with the `.superRefine(...)` above. The `z.object({...})` stays the same except `password` and `confirmPassword` drop the `.min(8)` and `.min(1)` constraints (those are now in `superRefine`).

---

## Step 15 — Tests

### 15a. `tests/Pena_e_Arte.UnitTests/Application/Auth/OAuthLoginCommandTests.cs`

Write unit tests covering:

1. **Happy path — Google** — valid provider + idToken, validator returns `OAuthUserInfo`, `LoginWithVerifiedEmailAsync` succeeds → returns `AuthResponse` with access token.
2. **Happy path — Apple** — same for Apple provider.
3. **Unknown provider** — `provider = "facebook"` → throws `BusinessRuleViolationException`.
4. **No account found** — `LoginWithVerifiedEmailAsync` returns `(false, null, "No account found...")` → throws `BusinessRuleViolationException`.
5. **Invalid ID token** — `ValidateGoogleTokenAsync` throws `InvalidOperationException` → propagates up.

Mock `IOAuthTokenValidator` with NSubstitute.

### 15b. `tests/Pena_e_Arte.UnitTests/Infrastructure/OAuthTokenValidatorTests.cs`

Write unit tests covering:

1. **JWKS caching** — second call to `ValidateGoogleTokenAsync` hits the cache, not the HTTP client.
2. **Cache miss** — first call fetches from `GoogleJwksUrl` and stores in cache.

These tests require an in-memory `IDistributedCache` (use `MemoryDistributedCache` from `Microsoft.Extensions.Caching.Memory` — already in the project). Mock `IHttpClientFactory` to return a handler that returns a fake JWKS payload.

> Note: Testing actual JWT signature validation requires constructing a real RS256 JWKS key pair. This is acceptable complexity — generate an RSA key, sign a test JWT with it, serve the public JWKS from the mock HttpClient, and validate. Use `System.Security.Cryptography.RSA` (BCL, no new packages).

### 15c. Frontend: `LoginPage.test.tsx`

Add tests:

1. **OAuth buttons render** — `"Continue with Google"` and `"Continue with Apple"` buttons are in the document.
2. **Google OAuth success** — mock `useGoogleSignIn` to resolve with a fake token; mock `POST /api/v1/auth/oauth/login` to return a valid JWT; clicking the button navigates to role home.
3. **OAuth error shown** — mock `useGoogleSignIn` to resolve, but `POST /api/v1/auth/oauth/login` returns 401; error text is displayed.

For mocking `useGoogleSignIn` in tests, use `vi.mock("@/shared/hooks/useGoogleSignIn")` (Vitest):

```tsx
vi.mock("@/shared/hooks/useGoogleSignIn", () => ({
  useGoogleSignIn: () => () => Promise.resolve("fake-google-id-token"),
}));

vi.mock("@/shared/hooks/useAppleSignIn", () => ({
  useAppleSignIn: () => () => Promise.resolve("fake-apple-id-token"),
}));
```

---

## Step 16 — Architecture docs

Update `docs/claude/architecture.md`:

Under **Feature Module Map**, add:

```
OAuth Sign-In    Backend:  POST /api/v1/auth/oauth/login   (AllowAnonymous)
                           POST /api/v1/auth/oauth/register (AllowAnonymous)
                           OAuthLoginCommand, RegisterOAuthUserCommand
                           IOAuthTokenValidator / OAuthTokenValidator
                           IIdentityService.LoginWithVerifiedEmailAsync
                           IIdentityService.CreateOAuthUserAsync
                 Frontend: OAuthButtons (shared component)
                           useGoogleSignIn, useAppleSignIn (shared hooks)
                           LoginPage — "Continue with Google/Apple"
                           RegisterStudioPage — OAuth path in step 2
                 Notes:    JS SDKs loaded from CDN in index.html. No npm packages.
                           JWKS fetched via IHttpClientFactory, cached in Redis 1h.
                           Backend validates ID token signature — frontend sends raw token.
                           Apple Sign In requires HTTPS even in development.
```

Under **Decisions Log**, add:

```
2026-06-25  OAuth uses frontend-first ID token flow (not backend redirect flow).
            Rationale: SPA with no new npm packages; CDN script + window.google / window.AppleID.
            Backend validates ID token with existing JwtSecurityTokenHandler + provider JWKS.
            CreateOAuthUserAsync uses userManager.CreateAsync(user) with no password (passwordless).
            Apple only returns name on first sign-in; subsequent sign-ins omit it — acceptable.
            Apple Sign In requires HTTPS; local dev must use a proxy or ngrok tunnel.
```

---

## Step 17 — Build and test

```bash
cd "Pena e Arte"
dotnet build
dotnet test
```

```bash
cd frontend
pnpm build
pnpm test
```

All existing tests must still pass. Zero TypeScript `any` types. Zero new packages in `package.json` or `.csproj`.

---

## Done checklist

- [ ] `appsettings.json` — `Google:ClientId` and `Apple:ClientId` sections added
- [ ] `GoogleOptions` / `AppleOptions` option classes created
- [ ] `index.html` — Google GSI and Apple Sign In scripts added
- [ ] `google-identity.d.ts` — global type declaration
- [ ] `apple-id.d.ts` — global type declaration
- [ ] `.env.example` — `VITE_GOOGLE_CLIENT_ID` and `VITE_APPLE_CLIENT_ID` documented
- [ ] `useGoogleSignIn` hook created
- [ ] `useAppleSignIn` hook created
- [ ] `IOAuthTokenValidator` interface + `OAuthUserInfo` record
- [ ] `OAuthTokenValidator` implementation (JWKS fetch, Redis cache, JWT validation)
- [ ] `IIdentityService` — `LoginWithVerifiedEmailAsync` + `CreateOAuthUserAsync` added
- [ ] `IdentityService` — both new methods implemented
- [ ] `OAuthLoginRequest` / `RegisterOAuthUserRequest` contracts
- [ ] `OAuthLoginCommand` + handler
- [ ] `RegisterOAuthUserCommand` + handler
- [ ] `OAuthLoginValidator` + `RegisterOAuthUserValidator`
- [ ] `AuthEndpoints` — 2 new routes (`/oauth/login`, `/oauth/register`)
- [ ] `authApi.ts` — `oauthLogin` + `oauthRegister` mutations
- [ ] `OAuthButtons` shared component
- [ ] `LoginPage.tsx` — `OAuthButtons` integrated
- [ ] `RegisterStudioPage.tsx` — OAuth path in step 2, schema updated with `superRefine`
- [ ] Backend unit tests: 5 command tests + 2 validator tests
- [ ] Frontend unit tests: 3 OAuth tests added to `LoginPage.test.tsx`
- [ ] `architecture.md` updated
- [ ] `dotnet build` passes
- [ ] `dotnet test` passes
- [ ] `pnpm build` passes
- [ ] `pnpm test` passes
