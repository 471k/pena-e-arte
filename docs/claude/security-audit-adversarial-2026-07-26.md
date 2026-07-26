# Full Adversarial Security Pass — 2026-07-26

**Requested by:** Phi
**Branch audited:** `main` @ `3039e58` ("fix: make mysql's host port publish overridable via MYSQL_HOST_PORT (#39)")
**Scope:** End-to-end, cross-cutting security review of the branch as it stands today — not a
role-scoped QA pass (those have run repeatedly over the past several weeks; see `architecture.md`'s
QA-pass log) and not a re-run of the 2026-07-20 industry-feature-parity audit. This is a dedicated
adversarial read of auth, tenant isolation, real-time (SignalR), file handling, secrets, rate
limiting, and webhook trust boundaries, written the way an external pen-tester would approach the
repo: assume every client input is hostile, assume every already-documented "this is fine because…"
justification might be wrong, and verify against live source rather than trusting prior write-ups.

**Method:** Read every `*Endpoints.cs` file in `Pena_e_Arte.API/Endpoints/` for authorization
coverage; read the authentication, CORS, rate-limiting, and Hangfire wiring in
`Pena_e_Arte.API/Extensions/` and `Program.cs`; read all four SignalR hubs and every call site of
`IRealtimeNotifier.NotifyStudioAsync`; read the Stripe webhook handlers and the R2 presigned-upload
path; cross-referenced findings against `docs/claude/architecture.md`'s `IgnoreQueryFilters()`
table, `AllowAnonymous` exceptions table, and QA-pass log so nothing already fixed gets
re-reported as new. Grounded severity calls against OWASP Top 10:2025 (confirmed current via web
search — A01 Broken Access Control now explicitly names BOLA/BFLA as the dominant pattern in
API-heavy applications) and OWASP ASVS, per this project's own industry-benchmarking rule.

**Verdict up front:** one **Critical/P0** finding — a systemic, codebase-wide broken-object-level-
authorization bug in the real-time layer — dominates this report. Everything else found is real but
comparatively lower severity, several of them defense-in-depth gaps the codebase has already
partially, explicitly flagged as "tighten in production" without a tracked follow-up. This audit
did **not** find SQL injection, XSS-via-unescaped-render, missing FluentValidation coverage, or
weak webhook signature validation — those categories are already reasonably well defended and are
also covered by the CI `guardrails`/CodeQL jobs added 2026-07-26, so this pass focused its manual
effort on the things static analysis structurally cannot catch: authorization logic and
architectural trust boundaries.

---

## Finding index

| # | Severity | Area | One-line summary |
|---|---|---|---|
| 1 | **Critical (P0)** | Real-time / tenant isolation | All three studio-scoped SignalR hubs let any authenticated user join any studio's group with zero membership check — full cross-tenant PII/PHI broadcast leak |
| 2 | High (P1) | Rate limiting / network trust | `ForwardedHeadersOptions` trusts all proxies — X-Forwarded-For spoofing can bypass the login/register brute-force throttle |
| 3 | Medium-High (P1) | Auth | `reset-password`, `refresh`, `verify-email` still carry no rate limiting — confirmed still open, not fixed since first flagged 2026-07-02 |
| 4 | Medium (P2) | Ops / config integrity | Hangfire dashboard's documented Basic-Auth env vars (`HANGFIRE_DASHBOARD_USERNAME`/`PASSWORD`) are never read by any code — dead configuration, unverified actual access path |
| 5 | Medium (P2) | Auth | No application-level minimum-strength guard on `Jwt:SecretKey` — protection against a weak/empty signing key depends entirely on `docker-compose.yml`, not on the app itself |
| 6 | Medium (P2) | File upload | R2 presigned-upload object keys are fully client-supplied (not server-generated) — same-tenant overwrite risk, and declared `Content-Type` isn't verified against actual uploaded bytes |
| 7 | Medium (P2) | Cost / availability | No rate limiting on authenticated endpoints that call out to Stripe on every request (`CreatePaymentIntent`, `CreateCheckout`, `RefundPayment`, `CaptureDeposit`) |
| 8 | Low (P3) | CORS | `AllowAnyOrigin()` dev fallback fails open silently if `Cors:AllowedOrigins` is empty in production, with no startup warning |
| — | Pass | Webhooks | Stripe signature verification (`EventUtility.ConstructEvent`) correctly implemented, fails closed, logs no PII |
| — | Pass | Tenant isolation (EF Core) | All 40 `IgnoreQueryFilters()` usages remain centrally tracked and narrow; spot-checked sample matches documented purpose |
| — | Pass | Background jobs | Hangfire job method signatures pass IDs, not names/emails/PII, as arguments |

---

## Finding 1 — Critical (P0): Cross-tenant broken object-level authorization in every "studio" SignalR hub

### The defect

`ScheduleHub`, `DesignHub`, and `NotificationHub` — three of this app's four real-time hubs — each
expose an identical, unguarded method:

```csharp
// Pena_e_Arte.Infrastructure/Hubs/ScheduleHub.cs (and DesignHub.cs, NotificationHub.cs — byte-for-byte identical)
[Authorize]
public class ScheduleHub : Hub
{
    public async Task JoinStudio(string studioId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"studio:{studioId}");

    public async Task LeaveStudio(string studioId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"studio:{studioId}");
}
```

`[Authorize]` only proves the caller has *some* valid JWT — any role, any tenant. `JoinStudio` never
checks that the caller actually belongs to `studioId`. Any authenticated `client`, `artist`, `owner`,
or `issuer` — of **any studio on the platform** — can call:

```js
connection.invoke("JoinStudio", "<any other studio's GUID>")
```

against `/hubs/schedule`, `/hubs/design`, or `/hubs/notification`, and will be added to that
studio's `studio:{id}` group with no further check, ever, for the life of the connection.

### Why this is worse than it looks — the blast radius

`Pena_e_Arte.Infrastructure/Services/RealtimeNotifier.cs` routes every `NotifyStudioAsync(studioId,
eventName, payload)` call to whichever of the three hubs owns that event name, then broadcasts
`payload` verbatim to `Clients.Group($"studio:{studioId}")`. Grepping every call site of
`NotifyStudioAsync` across `Pena_e_Arte.Application` shows this is not a narrow leak — it is the
event bus for nearly the entire product:

```
CreateAppointmentCommand.cs            → "AppointmentCreated"        (full AppointmentResponse)
ConfirmAppointmentCommand.cs           → "AppointmentConfirmed"      (full AppointmentResponse)
RescheduleAppointmentCommand.cs        → "AppointmentUpdated"        (full AppointmentResponse)
CancelAppointmentCommand.cs            → "AppointmentCancelled"      (full AppointmentResponse)
CompleteAppointmentCommand.cs          → "AppointmentCompleted"      (full AppointmentResponse)
MarkNoShowCommand.cs                   → "AppointmentNoShow"         (full AppointmentResponse)
SendAppointmentCreatedNotificationCommand.cs
SendAppointmentConfirmationCommand.cs
SendAppointmentCancellationCommand.cs  → "NotificationReceived"      (NotificationLogResponse —
AppointmentReminderJob.cs                                             includes RecipientName, i.e.
                                                                       the client's real first+last
                                                                       name, plus Subject/Body text
                                                                       containing appointment date,
                                                                       time, duration, and free-text
                                                                       appointment Notes)
UploadDesignRevisionCommand.cs         → "DesignUploaded"            (DesignRevisionResponse —
ReviewDesignCommand.cs                 → "DesignApproved" /            design image URLs)
                                          "DesignChangeRequested"
SendDesignReviewNotificationCommand.cs → "NotificationReceived"
SendConsentFormSignedNotificationCommand.cs → "NotificationReceived" (consent-form / health-intake
SendIntakeFormSubmittedNotificationCommand.cs → "NotificationReceived"  submission notices)
CreatePaymentIntentCommand.cs          → "PaymentIntentCreated"      (PaymentResponse — amounts)
RefundPaymentCommand.cs                → "PaymentRefunded"           (PaymentResponse)
CaptureDepositCommand.cs               → "DepositCaptured"           (PaymentResponse)
MarkPaymentAuthorizedCommand.cs        → "NotificationReceived"
SendPaymentRefundedNotificationCommand.cs / SendDepositCapturedNotificationCommand.cs
```

In other words: an attacker who is a legitimate, authenticated client of *their own* studio (the
lowest-privilege real account on the platform) can, with zero additional exploitation, silently
watch every other studio's live feed of client names, appointment times and free-text notes,
design-approval activity, consent/intake-form submission events, and payment/refund/deposit
activity — for as long as their SignalR connection stays open and however many studios' GUIDs they
choose to join. This is squarely OWASP Top 10:2025 **A01 Broken Access Control**, the
Broken-Object-Level-Authorization subclass the 2025 revision calls out explicitly as the most
exploited pattern in modern API-heavy applications — except here it's the WebSocket/SignalR layer,
which the CI `guardrails` job's endpoint-authorization heuristic (correctly) doesn't scan at all,
and which EF Core's global query filters (the codebase's primary tenant-isolation mechanism) have no
reach into whatsoever. This is a gap class, not a single endpoint miss.

### Why it wasn't caught already

`architecture.md`'s "Support Escalation — 2026-07-21" section documents that an `8-angle`
`/code-review high` pass on that diff *did* find and fix the identical defect in the new
`SupportHub.JoinTicket`, with this reasoning:

> `ScheduleHub` broadcasts studio-wide data any studio member already sees; this hub broadcasts a
> private two-party conversation. Fixed by validating ownership inside `JoinTicket` itself...

That reasoning is only correct if the caller of `ScheduleHub.JoinStudio` is actually a member of the
studio they're joining — which is exactly the check that was never added to `JoinStudio` itself. The
2026-07-21 review fixed the *instance* of the bug it was looking directly at (`SupportHub`) without
re-examining whether the premise it used to wave off the other three hubs ("any studio member
already sees this") was actually being enforced anywhere, and it wasn't. This is precisely the kind
of gap a single end-to-end adversarial pass is supposed to catch that role-scoped, feature-scoped QA
passes structurally can't: nobody's diff this session touched `ScheduleHub`/`DesignHub`/
`NotificationHub`, so no review ever looked at them again after the assumption was made.

### Fix shape (do not build here — this project does not touch code; see the companion overnight prompt)

Mirror `SupportHub.JoinTicket`'s own fix exactly: each hub's `JoinStudio` must resolve the caller's
own studio membership from `Context.User` claims (never `ICurrentUser`/`ICurrentTenant` — per the
2026-07-21 finding, those are unpopulated for hub invocations since `/hubs` paths are in
`TenantMiddleware.ExemptPrefixes`) and reject/no-op if the caller's `tenant_id` claim doesn't match
the requested `studioId`, with one exception to design carefully: `issuer`-role connections need
cross-tenant join capability for legitimate platform-support purposes (mirroring how the issuer role
already gets legitimate cross-tenant reads elsewhere via the `IgnoreQueryFilters()` table) — this
needs an explicit, reviewed decision, not a silent carve-out. See the companion overnight prompt for
the exact phase spec.

---

## Finding 2 — High (P1): Forwarded-header trust allows brute-force-throttle bypass

`Program.cs`:

```csharp
// Without this, the API only sees the K8s/Nginx ingress IP in RemoteIpAddress,
// so every client shares one rate-limit bucket. KnownNetworks/KnownProxies are
// left empty (trust all proxies) — acceptable on a private cluster network;
// tighten to the ingress CIDR in production.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});
```

`RateLimitingExtensions.cs`'s `auth` policy (10 req/min — the only defense against login/register/
oauth/forgot-password brute-forcing) partitions purely on `httpContext.Connection.RemoteIpAddress`,
which `ForwardedHeadersMiddleware` rewrites from the client-supplied `X-Forwarded-For` header
whenever `KnownProxies`/`KnownNetworks` isn't restricted. With both empty, any request whose
immediate TCP peer isn't independently validated gets to set its own perceived IP per request —
trivially defeating the fixed-window IP-keyed limiter by sending a different `X-Forwarded-For` value
on every attempt. The comment in the code already identifies this exact risk and defers it to "in
production" with no tracked follow-up item anywhere in `architecture.md`'s Decisions Log or backlog.
This needs either a confirmed statement that the K3s ingress topology makes direct-to-pod access
structurally impossible (a NetworkPolicy claim that should be verified, not assumed) or
`KnownNetworks` needs to be set to the actual ingress CIDR before this is genuinely closed.

---

## Finding 3 — Medium-High (P1, re-confirmed still open): No rate limiting on `reset-password`/`refresh`/`verify-email`

Confirmed directly against live `AuthEndpoints.cs`:

```csharp
group.MapPost("/reset-password", ResetPassword).AllowAnonymous();       // no .RequireRateLimiting
group.MapPost("/refresh", Refresh).AllowAnonymous();                    // no .RequireRateLimiting
group.MapGet("/verify-email", VerifyEmail).AllowAnonymous();            // no .RequireRateLimiting
```

`architecture.md` (line ~928) already documents this as a known gap from the 2026-07-02 Redis
rate-limiting work ("out of scope for this audit"). It is re-surfaced here, not as a new discovery,
but because **it is still unresolved on `main` today** and this is an end-to-end pass whose job is
to report where things stand, not just what's new. `refresh` in particular is a repeatable,
unlimited-attempt surface for a stolen or guessed refresh token; unlimited attempts against
`reset-password` and `verify-email` is a standard OWASP ASVS L2 expectation regardless of token
entropy (defense-in-depth, not "the token is long enough so it doesn't matter"). Every competitor in
this product's benchmark set (Vagaro/Fresha/Boulevard-tier vertical SaaS) treats rate limiting as
uniform across all unauthenticated auth-adjacent endpoints, not just login.

---

## Finding 4 — Medium (P2): Hangfire dashboard's Basic-Auth configuration is dead code

`docker-compose.yml` requires, fail-closed:

```yaml
Hangfire__DashboardUsername: ${HANGFIRE_DASHBOARD_USERNAME:?Set HANGFIRE_DASHBOARD_USERNAME in .env — do not default to admin}
Hangfire__DashboardPassword: ${HANGFIRE_DASHBOARD_PASSWORD:?Set HANGFIRE_DASHBOARD_PASSWORD in .env — do not default to admin}
```

`appsettings.json` declares matching keys (`Hangfire:DashboardUsername`/`DashboardPassword`, both
`""`). But `HangfireDashboardAuthFilter.cs` — the only authorization check actually wired to
`/hangfire` via `app.UseHangfireDashboard(...)` in `Program.cs` — never reads either value:

```csharp
public bool Authorize(DashboardContext context)
{
    HttpContext httpContext = context.GetHttpContext();
    return httpContext.User.Identity?.IsAuthenticated == true &&
           httpContext.User.IsInRole("issuer");
}
```

This is purely a JWT-bearer-scheme role check. Two consequences, and this report cannot say for
certain which is true without a live test (flagging as "verify," not asserting): (a) if a plain
browser navigation to `/hangfire` never carries an `Authorization: Bearer` header (typical for an
SPA that stores its JWT in memory and only attaches it to `fetch`/XHR calls, not full-page
navigations), the dashboard may be **completely unreachable by its intended issuer operators** —
broken tooling, not a vulnerability, but worth knowing before someone spends an afternoon debugging
"why can't I log into Hangfire." (b) If some other code path does attach the bearer token to this
navigation, then `HANGFIRE_DASHBOARD_USERNAME`/`PASSWORD` are simply inert — required, enforced,
and doing nothing. Either way, the required env vars create a false impression that this ops surface
(which can view/retry/trigger background jobs) has two independent layers of defense when it only
has one, unverified end-to-end.

---

## Finding 5 — Medium (P2): No application-level guard on JWT signing-key strength

`AuthenticationExtensions.cs`:

```csharp
IssuerSigningKey = new SymmetricSecurityKey(
    Encoding.UTF8.GetBytes(configuration["Jwt:SecretKey"]!)),
```

No length or entropy check. The only thing preventing a weak or empty signing key today is
`docker-compose.yml`'s `${JWT_SECRET_KEY:?Set JWT_SECRET_KEY in .env (min 32 chars)}` — a
deployment-file-level control, not an application-level one. `appsettings.json` itself ships
`"SecretKey": ""`. Any deployment path that doesn't go through this specific compose file (a
hand-written K3s manifest, a differently-provisioned VM, `dotnet run` with a stray or forgotten
environment) has no code-level backstop and will start up successfully — no crash, no warning —
signing every JWT in the system with whatever ends up in `Jwt:SecretKey`, including an empty string.
Recommend a fail-fast guard inside `AddApiAuthentication` itself (throw if the key is null/empty or
under a minimum byte length), independent of any particular deployment method, matching the
"don't rely on one specific file to be the only thing standing between the app and an insecure
default" principle already applied elsewhere (e.g., `StripeDemoSeeder`'s self-guard against running
against a live key, mentioned in `Program.cs`'s own comments).

---

## Finding 6 — Medium (P2): Client-supplied R2 object keys; unverified upload content-type

`GetPresignedUploadUrlHandler`:

```csharp
string scopedKey = $"{tenant.StudioId}/{query.Request.ObjectKey.TrimStart('/')}";
(string uploadUrl, string publicUrl) =
    await r2.GeneratePresignedUploadUrlAsync(scopedKey, query.Request.ContentType, ct);
```

`GetPresignedUploadUrlValidator` correctly blocks `..` and enforces a `ContentType` allow-list
(`image/jpeg`, `image/png`, `image/webp`, `application/pdf`) — both good controls, no injection/
traversal path found. Two residual issues:

1. **`ObjectKey` is entirely attacker-chosen** (any string ≤ 500 chars, no `..`). Nothing enforces
   per-entity uniqueness or namespacing beyond the tenant prefix — a malicious `ClientAndAbove` user
   of a studio could deliberately choose an `ObjectKey` matching a path another user/entity in the
   *same* tenant is already using, silently overwriting it (no conditional-write/if-none-match
   semantics are applied to the presigned PUT). Recommend the API generate the key's unique suffix
   itself (e.g. append a fresh server-side GUID) rather than trusting client input for anything
   beyond a fixed, server-chosen folder prefix.
2. **The declared `ContentType` is never verified against the actual bytes uploaded** — the browser
   PUTs directly to R2, bypassing the API entirely after the presigned URL is issued. A client could
   request a presigned URL with `ContentType: image/png` and then upload arbitrary bytes (e.g., an
   HTML/SVG payload with an embedded script). Whether this is exploitable depends on whether R2's
   public CDN path serves objects with `X-Content-Type-Options: nosniff` (preventing browser content
   sniffing) — this needs to be verified against the live R2/Cloudflare configuration, which is
   outside this repo's source. Flagging as a "verify infra config" item, not asserting it's
   currently exploitable.

---

## Finding 7 — Medium (P2): No rate limiting on authenticated, cost-bearing Stripe-calling endpoints

`RateLimitingExtensions.cs` only defines `auth`/`public-write`/`public-read` policies.
`architecture.md` documents the deliberate decision that authenticated endpoints don't need one
("volume is controlled by JWT auth"). That's a reasonable default for ordinary CRUD, but
`CreatePaymentIntent`, `CreateCheckout`/`CreateCheckout/finalize`, `RefundPayment`, and
`CaptureDeposit` each make a live call to the platform's single aggregator Stripe account per
`architecture.md`'s "Payment Architecture — Card & Cash Only" section. A compromised or malicious
`owner`-role account currently has no in-app throttle stopping it from generating an unbounded
number of Stripe API calls — a cost-abuse and platform-wide-Stripe-rate-limit risk (since it's one
shared aggregator account across every tenant, per the architecture doc), not merely a
single-tenant nuisance. Recommend a dedicated, generous-but-present rate-limit policy specifically
for billing-mutation endpoints, distinct from the existing three policies.

---

## Finding 8 — Low (P3): Silent CORS fail-open on missing production config

`CorsExtensions.cs` falls back to `AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()` whenever
`Cors:AllowedOrigins` is unset — a sensible local-dev default (and correctly avoids the invalid
`AllowAnyOrigin()` + `AllowCredentials()` combination by only adding credentials in the
explicit-origins branch). But there is no startup-time check that `ASPNETCORE_ENVIRONMENT=Production`
combined with an empty origins list is a misconfiguration worth a loud warning (or a hard failure) —
today it would silently and successfully open CORS to every origin in production if the env var
were ever left unset. Low severity because `docker-compose.yml` does set
`Cors__AllowedOrigins__0` explicitly, but worth a fail-fast guard for any deployment path that
doesn't go through that file, same theme as findings 4 and 5.

---

## Confirmed clean (verified, not assumed)

- **Stripe webhook signature validation** (`HandleBillingWebhook`/`HandleConnectWebhook` in
  `BillingEndpoints.cs`) uses `Stripe.EventUtility.ConstructEvent` with the correct per-endpoint
  secret, catches `StripeException` and returns `401` on a bad signature, and logs only
  `stripeEvent.Type` — no PII, no raw payload. No changes recommended.
- **`IgnoreQueryFilters()` usages** — all 40 entries in `architecture.md`'s approved-usages table
  were spot-checked against a sample of their actual call sites (issuer-only reads, anonymous public
  portfolio reads, webhook-driven cross-tenant payment lookups by Stripe intent ID) and each matches
  its documented purpose and caller restriction. No new unauthorized usage found.
- **Hangfire job argument shapes** — every job method signature grepped
  (`SendReminderAsync(Guid, string, ...)`, `ExecuteAsync(Guid, ...)`, etc.) passes IDs, not
  names/emails, as method arguments, keeping Hangfire's own storage/dashboard free of PII by
  construction. (The PII exposure in this report comes from Finding 1's SignalR broadcast, not from
  Hangfire's own persisted job data.)
- **FluentValidation coverage, EF Core parameterization, and general injection surface** — not
  independently re-audited line-by-line in this pass beyond the file-presign validator, since the
  2026-07-26 CI pipeline's CodeQL job (both `csharp` and `javascript-typescript` matrix legs) now
  runs on every push/PR and a weekly schedule, and is the better tool for that specific class of
  bug. This pass's manual effort was deliberately spent on authorization/architecture logic
  CodeQL cannot reason about.

---

## Industry-standard benchmark note (CLAUDE.md rule #6)

OWASP Top 10:2025 (confirmed current — released 2025, A01 Broken Access Control explicitly folds in
BOLA/BFLA as the dominant API-security failure mode) and OWASP ASVS are the correct benchmark for
this kind of cross-cutting security pass, not the vertical booking-SaaS UX benchmark set
(Vagaro/Fresha/Boulevard/etc.) used elsewhere in this project's audits — those competitors' *product
feature* bar isn't the relevant comparison for "is the app itself secure." Where a competitor
comparison is meaningful (Finding 3's rate-limiting-everywhere expectation), it's noted inline.

Sources consulted: [OWASP Top Ten 2025](https://owasp.org/Top10/2025/), [OWASP Top 10 for 2025: What's Changed](https://outpost24.com/blog/owasp-top-10-2025-what-changed/), [What Changed in OWASP Top 10 2025 — Qualys](https://blog.qualys.com/qualys-insights/2026/06/15/what-changed-in-owasp-top-10-2025-and-recommendations-for-each-category).

---

## Help-menu sync (CLAUDE.md rule #7)

**Verdict: no Help Menu / user manual / onboarding-tour update needed for this audit document
itself** — it is an internal engineering artifact with zero user-visible surface. The fixes this
report recommends (see companion overnight prompt) are also expected to carry a "no Help change
needed" verdict for the same reason (backend authorization/config hardening, no new or changed
user-visible screen, field, or workflow) — but that judgment is restated explicitly per-phase in the
overnight prompt itself, not assumed here.

---

## Next step

This project does not implement fixes. See the companion document,
`docs/claude/overnight-prompt-security-remediation-2026-07-26.md`, for a fully-specified overnight
master prompt that fixes Findings 1 through 8 in the main "Pena e Artë - Engineering" project,
in priority order, with exact current code, exact target code, and the do-not-touch boundary.
