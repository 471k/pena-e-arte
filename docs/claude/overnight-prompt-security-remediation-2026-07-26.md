# Overnight Master Prompt — Security Remediation (Adversarial Pass Findings)

**Date:** 2026-07-26
**Requester:** Phi
**Origin:** `docs/claude/security-audit-adversarial-2026-07-26.md` — a full end-to-end adversarial
security pass, distinct from and broader than the role-scoped QA passes run over the past several
weeks. That report's full evidence and reasoning is required reading; this prompt is the
implementation-ready fix spec for its 8 findings.
**Mode:** Fully autonomous. No user present. Run until every phase exits clean.
**Run with:** `claude --dangerously-skip-permissions`
**Before starting:**
```
git add -A && git commit -m "chore: pre-security-remediation checkpoint"
git checkout -b fix/security-remediation-2026-07-26
```

---

## 1. Read first

```
CLAUDE.md                                              — rules 1-7, all apply tonight
docs/claude/security-audit-adversarial-2026-07-26.md   — full findings, evidence, severity, exact
                                                          file:line citations for everything below
docs/claude/architecture.md                            — SignalR event naming conventions section,
                                                          "Support Escalation — 2026-07-21" (the
                                                          SupportHub.JoinTicket fix this prompt's
                                                          Phase 1 must mirror), IgnoreQueryFilters
                                                          table, Decisions Log
docs/claude/backend.md
docs/claude/conventions.md
Pena_e_Arte.Infrastructure/Hubs/SupportHub.cs           — the fix pattern to mirror in Phase 1
```

Re-verify every citation against current source before acting on it — the audit report is hours
old, not days, but this codebase moves fast; if a citation is stale, trust the live source and note
the discrepancy in the deliverable rather than silently proceeding on outdated information.

---

## 2. Decisions already made vs. decisions to flag

### 2.1 Already decided — implement as specified, do not re-litigate

1. **Phase order is fixed by severity**, not convenience: Finding 1 (Critical) is Phase 1 and gates
   nothing else, but must land first and completely before any other phase is considered started,
   given its severity. Phases 2-7 map 1:1 to Findings 2-8 in the audit report's numbering and may
   proceed in any order relative to each other once Phase 1 is done and verified.
2. **Finding 1's fix mechanism**: each hub validates the caller's own `tenant_id` claim against the
   requested `studioId` inside `JoinStudio` itself, reading directly from `Context.User` (never
   `ICurrentUser`/`ICurrentTenant`, which are unpopulated for hub invocations — `/hubs` paths are in
   `TenantMiddleware.ExemptPrefixes`, confirmed in the 2026-07-21 `SupportHub` fix). This is not an
   open design question — it is the exact, already-proven pattern in this codebase.
3. **Issuer cross-tenant join is an explicit, narrow exception, not a silent bypass.** An `issuer`
   caller legitimately needs to join any studio's group for platform support purposes (mirroring
   every other issuer cross-tenant capability already documented in the `IgnoreQueryFilters()`
   table). Implement this as an explicit `if (role == "issuer") return;` early-allow before the
   tenant-match check, not by weakening the check itself for everyone.
4. **Findings 4, 5, 8 (Hangfire dead config, JWT key guard, CORS fail-open guard) are startup-time
   hardening, not behavior changes** — implement as fail-fast guards that only fire on genuinely
   missing/weak/misconfigured values. Do not change any currently-correct configured value.
5. **Finding 6's fix (server-generated object-key suffix) changes the `PresignUploadRequest`
   contract.** Read `Pena_e_Arte.Contracts/Requests/PresignUploadRequest.cs` and every frontend
   caller of the presign endpoint before deciding the exact shape of the change — this touches
   frontend upload call sites (`frontend/src/**`), which is normally out of this backend-focused
   prompt's scope (see §4), but this one finding requires a coordinated frontend change since the
   frontend currently supplies the full key. Treat this as the one narrow, explicitly-scoped
   exception to §4's frontend boundary — nothing else in this prompt touches `frontend/src/**`.

### 2.2 Flag, verify empirically, do not assume

1. **Whether a plain browser navigation to `/hangfire` ever carries an `Authorization: Bearer`
   header today.** Finding 4 flagged this as unverified. Test it empirically (real browser or curl
   without a manually-attached header) before deciding whether Hangfire access is currently broken
   (needs a different fix — e.g. wiring real bearer-token-via-cookie or documented curl-only access
   for issuers) or working via some mechanism this audit didn't find (in which case the dead
   username/password config still needs resolving one way or another — wire it as real Basic Auth
   defense-in-depth, or remove it and document why). Resolve this explicitly in the final summary,
   not silently.
2. **Whether the K3s ingress topology makes direct-to-API-pod access structurally impossible**
   (Finding 2). If there's a `NetworkPolicy` or equivalent already restricting this, cite it and
   downgrade Phase 2 to "confirmed already mitigated at the infra layer, no code change needed." If
   no such policy exists or can't be confirmed from this repo, implement the code-level fix
   (§6, Phase 2) regardless — don't leave a known gap open on the assumption an unverified external
   control covers it.
3. **Whether Cloudflare R2's public serving path sends `X-Content-Type-Options: nosniff`** (Finding
   6's second half). This is infrastructure/CDN configuration, likely outside this repo's source —
   if it can't be verified or changed from within this codebase, say so explicitly in the final
   summary and flag it as a "do not build blind" infra item for whoever manages the Cloudflare
   account, rather than silently dropping it.

### 2.3 Do not build blind — backlog, not built tonight

- **A full SignalR-layer authorization framework** (e.g., a generic `[RequireStudioMembership]`
  hub-method attribute) is a legitimate future refactor once this specific defect is closed in all
  three hubs, but building a new generic mechanism tonight risks under-testing it under time
  pressure for a P0 fix that needs to land correctly, not elegantly. Fix the three hubs directly
  and identically (matching `SupportHub`'s own precedent of an inline check, not a new abstraction),
  and note the generic-attribute idea as a fast-follow in the backlog section of the deliverable.
- **Redis-backed distributed rate limiting for a new "billing" policy tier** (Finding 7) should
  reuse the exact same `AddRedisPolicy` helper already in `RateLimitingExtensions.cs` — this is
  explicitly NOT a "do not build blind" item, it's directly in scope for Phase 7, called out here
  only to pre-empt over-engineering it into something new.
- **Support-impersonation tooling** and any other item already on `architecture.md`'s standing
  backlog is untouched tonight — this prompt is scoped exactly to the audit report's 8 findings,
  nothing else.

---

## 3. Scope boundary — do not touch

- `frontend/src/**` — **except** the one narrowly-scoped presign-request-shape change required by
  Phase 6 (§2.1 item 5) and, if Finding 1's fix requires the frontend's `JoinStudio` invocation to
  pass anything additional (it should not — the fix is entirely server-side, reading from the
  existing JWT already attached to the hub connection — confirm this and do not touch frontend hub
  client code unless you discover a genuine need, in which case flag it explicitly rather than
  silently expanding scope).
- Any Stripe/Resend/Twilio configuration values themselves (only the rate-limit policy wrapping
  calls to them, per Phase 7).
- `docs/claude/architecture.md`'s `IgnoreQueryFilters()` Approved Usages table — none of tonight's
  fixes add a new `IgnoreQueryFilters()` call; do not add a row to that table.
- The CI pipeline (`.github/workflows/*`) and observability stack
  (`docker/observability/**`, the five new `docker-compose.yml` services) — both separate,
  already-completed initiatives from earlier today, unrelated to this remediation.
- Any migration or entity unrelated to the 8 findings above — this is a hardening pass, not a
  feature change; if a fix seems to require a new entity/migration beyond what's specified in a
  phase below, stop and flag it rather than inventing scope.

---

## 4. Constraints (restated per project standard)

- No new npm/NuGet packages without flagging it as a prerequisite decision first.
- No `useEffect` for data fetching (relevant only to Phase 6's narrow frontend touch, if any UI
  changes beyond the request payload are needed — confirm none are before assuming otherwise).
- TypeScript strict / no `any`. Explicit C# types, no unclear `var`.
- No business logic in endpoints — MediatR + FluentValidation only.
- Tenant isolation via EF Core global query filters everywhere except already-approved
  `IgnoreQueryFilters()` usages — unaffected tonight (see §3).
- Every endpoint keeps its existing `.RequireAuthorization()` policy unchanged unless a phase below
  explicitly says otherwise (none do — every fix tonight is either hub-level or startup-config-level,
  not endpoint-policy-level).
- Never log PII. Structured logs only. No secrets in source.
- Every fix ships with a test that would have caught the original defect — this is a security
  remediation; "trust me it's fixed" is not acceptable, a regression test proving the old exploit
  path now fails is required for every phase.

---

## PHASE 1 — Fix cross-tenant SignalR authorization (Finding 1, Critical/P0)

### Current code (verbatim, identical in all three files)

```csharp
// Pena_e_Arte.Infrastructure/Hubs/ScheduleHub.cs
// Pena_e_Arte.Infrastructure/Hubs/DesignHub.cs
// Pena_e_Arte.Infrastructure/Hubs/NotificationHub.cs
[Authorize]
public class ScheduleHub : Hub   // (DesignHub / NotificationHub respectively)
{
    public async Task JoinStudio(string studioId) =>
        await Groups.AddToGroupAsync(Context.ConnectionId, $"studio:{studioId}");

    public async Task LeaveStudio(string studioId) =>
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, $"studio:{studioId}");
}
```

### Required reading before writing the fix

`Pena_e_Arte.Infrastructure/Hubs/SupportHub.cs`'s `JoinTicket` method — the exact, already-reviewed
pattern for reading claims directly from `Context.User` inside a hub method and rejecting on
mismatch. Match its structure (claim names, exception/rejection style) rather than inventing a new
shape.

### Target behavior

For each of the three hubs:

1. Read the caller's `tenant_id` claim and role claim directly from `Context.User` (same claim
   names `TenantMiddleware`/`RequestLoggingEnrichment.cs` already use elsewhere:
   `ClaimTypes.NameIdentifier` for user id if needed, `"tenant_id"` for the studio claim, `role`
   claim for role — confirm exact claim type strings against how the JWT is actually issued, e.g.
   `Pena_e_Arte.Infrastructure`'s token-issuing service, before hardcoding a string that might not
   match).
2. If the caller's role is `issuer`: allow the join unconditionally (cross-tenant support access is
   a legitimate, already-established pattern for this role — see §2.1 item 3).
3. Otherwise: parse `studioId` as a `Guid` (reject non-Guid input rather than string-comparing) and
   compare it to the caller's own `tenant_id` claim. If they don't match, do **not** add the caller
   to the group — return without throwing (a hub method silently no-op-ing on an unauthorized join
   attempt is preferable to leaking information via an exception the client can distinguish from a
   generic failure; confirm this matches `SupportHub.JoinTicket`'s own error-handling shape and use
   the same approach for consistency, don't invent a different failure mode for these three hubs).
4. `LeaveStudio` needs no change — leaving a group you were never validly added to is harmless.
5. Apply the identical fix to `ScheduleHub`, `DesignHub`, and `NotificationHub` — do not fix one and
   leave the others "for later"; they are the same defect and must land together in this phase.

### Tests

- For each of the three hubs: a caller whose `tenant_id` claim matches `studioId` successfully joins
  the group (existing behavior preserved — regression test).
- For each of the three hubs: a caller whose `tenant_id` claim does **not** match the requested
  `studioId` does NOT get added to the group — assert directly against the hub's group membership
  (via a test double / `IHubCallerClients`+`IGroupManager` mock, matching however `SupportHub`'s own
  existing tests for `JoinTicket` are structured — reuse that test's exact technique).
- An `issuer`-role caller successfully joins any studio's group regardless of their own `tenant_id`
  claim (or lack thereof).
- A malformed (non-Guid) `studioId` is rejected without throwing an unhandled exception.
- Confirm via an integration-level test (or the closest equivalent this codebase's test suite
  already uses for hub behavior) that a real broadcast (`NotifyStudioAsync` with a fake studio id)
  is received only by a connection that successfully joined that studio's group — this is the test
  that actually proves the exploit path in the audit report is closed, not just that `JoinStudio`
  returns without error.

### Help sync (rule #7)

**Verdict: no Help Menu / user manual / onboarding-tour update needed.** This is a server-side
authorization fix with zero user-visible surface change — legitimate users joining their own
studio's group see no behavior difference at all. State this explicitly in the final summary.

### Industry-standard benchmark note (rule #6)

OWASP Top 10:2025 A01 (Broken Access Control) explicitly names BOLA/BFLA as the dominant pattern in
API-heavy applications — this fix closes exactly that class of gap in the real-time layer. No
vertical-SaaS competitor comparison is meaningful here; this is a foundational security-correctness
fix, not a feature-parity item.

---

## PHASE 2 — Restrict trusted forwarded-header proxies (Finding 2, High/P1)

### Current code

```csharp
// Pena_e_Arte.API/Program.cs
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
});
```

### Required investigation first

Determine, from this repo's own infra config (`docker-compose.yml`, any K3s manifests if present,
Nginx config if present in-repo) whether there's an already-enforced network boundary that makes
direct-to-API-pod access impossible. If you find one, cite it exactly in the deliverable and treat
this phase as "confirmed already mitigated, documented, no code change" — do not implement a change
whose necessity you can't support with evidence. If no such boundary is found or confirmable from
this repo, implement the fix below.

### Target behavior (if no infra-level mitigation is confirmed)

Set `KnownNetworks`/`KnownProxies` to the actual trusted proxy range rather than leaving both empty.
Since the exact production ingress CIDR isn't knowable from this repo alone, make it configurable
rather than hardcoding a guess:

```csharp
ForwardedHeadersOptions fwdOptions = new()
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
};
string? trustedProxyCidr = builder.Configuration["ForwardedHeaders:TrustedProxyCidr"];
if (!string.IsNullOrEmpty(trustedProxyCidr))
{
    (IPAddress network, int prefixLength) = ParseCidr(trustedProxyCidr); // implement or use an
                                                                          // existing CIDR-parsing
                                                                          // helper if one already
                                                                          // exists in this codebase
    fwdOptions.KnownNetworks.Add(new IPNetwork(network, prefixLength));
}
else
{
    logger.LogWarning("ForwardedHeaders:TrustedProxyCidr is not set — trusting all proxies for " +
        "X-Forwarded-For. This allows IP-based rate limiting to be bypassed. Set this in " +
        "production.");
}
app.UseForwardedHeaders(fwdOptions);
```

Add `ForwardedHeaders__TrustedProxyCidr` to `docker-compose.yml` (optional, `${...:-}`, not
fail-closed — an empty value should log the warning above, not crash a working dev environment) and
`.env.example` with a comment explaining what to set it to for the actual K3s ingress. Do not
guess a real CIDR value — leave it for whoever operates the cluster to fill in, and say so plainly
in the final summary.

### Tests

- A request with a spoofed `X-Forwarded-For` from an untrusted peer does NOT have its
  `RemoteIpAddress` rewritten when `TrustedProxyCidr` is unset or doesn't match — i.e. rate limiting
  still keys off the real connection IP, not the spoofed header, in the default/misconfigured case.
- A request from a peer within the configured trusted CIDR correctly has `RemoteIpAddress` rewritten
  from `X-Forwarded-For` (regression test for the legitimate ingress case).

### Help sync

No Help change needed — infra/networking hardening, zero user-visible surface.

---

## PHASE 3 — Rate-limit `reset-password`, `refresh`, `verify-email` (Finding 3, Medium-High/P1)

### Current code

```csharp
// Pena_e_Arte.API/Endpoints/AuthEndpoints.cs
group.MapPost("/reset-password", ResetPassword).AllowAnonymous();
group.MapPost("/refresh", Refresh).AllowAnonymous();
group.MapGet("/verify-email", VerifyEmail).AllowAnonymous();
```

### Target code

```csharp
group.MapPost("/reset-password", ResetPassword).AllowAnonymous().RequireRateLimiting("auth");
group.MapPost("/refresh", Refresh).AllowAnonymous().RequireRateLimiting("auth");
group.MapGet("/verify-email", VerifyEmail).AllowAnonymous().RequireRateLimiting("auth");
```

Reuse the existing `"auth"` policy (10 req/min, already defined in `RateLimitingExtensions.cs`) —
do not create a new policy for this; these three endpoints belong in the same bucket as
login/register/oauth/forgot-password per the audit report's reasoning. If 10 req/min proves too
restrictive for `refresh` specifically (a legitimate client might refresh its token more often than
10 times/minute across multiple tabs/devices), verify this against how the frontend actually calls
`/refresh` (check for a shared refresh-in-flight guard / token-refresh interceptor in
`frontend/src/**` reads only, no edits) before deciding whether `refresh` needs a distinct, more
generous policy — do not silently under-limit it just because the default might be inconvenient
without checking real usage first.

### Tests

- Each of the three endpoints returns `429` after exceeding the `auth` policy's limit within its
  window — mirror however the existing `login`/`register` rate-limit tests are structured.
- Confirm a legitimate `refresh` flow (matching real frontend call patterns) doesn't get spuriously
  rate-limited under normal use — if it would, this is the "flag, don't guess" moment from §2.2/§4
  discipline: implement the distinct policy instead of shipping a broken refresh flow.

### Help sync

No Help change needed — no user-visible behavior change for a legitimate user (429 only fires under
abuse-level request volume).

---

## PHASE 4 — Resolve Hangfire dashboard auth: verify, then wire correctly or remove dead config (Finding 4, Medium/P2)

1. Empirically determine (§2.2 item 1) whether `/hangfire` is reachable via normal browser
   navigation today given the JWT-bearer-only `HangfireDashboardAuthFilter`.
2. **If unreachable** (most likely, given SPA bearer-token storage patterns): implement real HTTP
   Basic Authentication as the actual access mechanism, finally consuming the
   `Hangfire:DashboardUsername`/`DashboardPassword` config that already exists and is already
   required fail-closed in `docker-compose.yml` — this is the simplest fix that makes the existing,
   already-enforced env vars actually do something, and matches how Hangfire dashboards are
   conventionally secured in ASP.NET Core apps that don't want to build a full SPA-auth bridge for
   an ops-only tool. Keep the existing `IsInRole("issuer")` check as an *additional* layer if a
   valid JWT happens to be present (defense in depth, not a replacement), but Basic Auth becomes the
   actual gate for normal use.
3. **If somehow already reachable** via some mechanism this audit didn't find: document that
   mechanism precisely in the deliverable, and still resolve the dead-config question — either wire
   Basic Auth as an additional layer (recommended, since a job dashboard exposing retry/trigger
   controls and job argument data warrants defense in depth) or remove the unused config and update
   `.env.example`/`docker-compose.yml` to stop requiring values nothing reads.
4. Update `HangfireDashboardAuthFilterTests.cs` to cover whichever final shape is implemented —
   the existing 6 tests must still pass unchanged if the role check is kept as an additional layer;
   add new tests for the Basic Auth path.

### Tests

- Correct username/password → dashboard access granted (assuming no valid issuer JWT present).
- Incorrect username/password → `401`, not silently falling through to some other check.
- Existing `IsInRole("issuer")` JWT-based tests continue to pass unchanged if kept as an additional
  layer.

### Help sync

No Help change needed — ops tooling, not part of the product surface any `client`/`artist`/`owner`/
`issuer` end-user role sees.

---

## PHASE 5 — Fail-fast JWT signing-key strength guard (Finding 5, Medium/P2)

### Current code

```csharp
// Pena_e_Arte.API/Extensions/AuthenticationExtensions.cs
IssuerSigningKey = new SymmetricSecurityKey(
    Encoding.UTF8.GetBytes(configuration["Jwt:SecretKey"]!)),
```

### Target code

Add an explicit guard before constructing the security key:

```csharp
string? secretKey = configuration["Jwt:SecretKey"];
if (string.IsNullOrEmpty(secretKey) || Encoding.UTF8.GetByteCount(secretKey) < 32)
{
    throw new InvalidOperationException(
        "Jwt:SecretKey must be set and at least 32 bytes (256 bits) — refusing to start with a " +
        "missing or weak JWT signing key. Set JWT_SECRET_KEY in your environment.");
}
```

Place this check at the top of `AddApiAuthentication`, before the `AddJwtBearer` call, so it fails
at DI-configuration time / application startup rather than on the first request. Confirm this
doesn't break any existing test that constructs the auth pipeline with a short test-only key — if it
does, update the test fixture's key to be ≥32 bytes rather than weakening the guard.

### Tests

- Startup with an empty `Jwt:SecretKey` throws `InvalidOperationException` with a clear message.
- Startup with a key under 32 bytes throws the same.
- Startup with a valid ≥32-byte key succeeds unchanged (regression test — every existing
  integration test that boots the API must still pass).

### Help sync

No Help change needed — startup-time configuration validation, invisible to end users.

---

## PHASE 6 — Server-generated R2 object-key suffix (Finding 6, Medium/P2)

### Current code

```csharp
// Pena_e_Arte.Application/Files/Queries/GetPresignedUploadUrlQuery.cs
public class GetPresignedUploadUrlHandler(IR2Service r2, ICurrentTenant tenant)
    : IRequestHandler<GetPresignedUploadUrlQuery, PresignUploadResponse>
{
    public async Task<PresignUploadResponse> Handle(
        GetPresignedUploadUrlQuery query, CancellationToken ct)
    {
        string scopedKey = $"{tenant.StudioId}/{query.Request.ObjectKey.TrimStart('/')}";
        (string uploadUrl, string publicUrl) =
            await r2.GeneratePresignedUploadUrlAsync(scopedKey, query.Request.ContentType, ct);
        return new PresignUploadResponse(uploadUrl, publicUrl);
    }
}
```

### Design decision (pre-resolved)

Read `Pena_e_Arte.Contracts/Requests/PresignUploadRequest.cs` and every frontend call site of the
presign endpoint (`frontend/src/**`, read-only until you confirm the exact shape needed) before
changing anything — the request currently carries a full client-chosen `ObjectKey` (e.g. something
like `"designs/revision-cover.jpg"`), which the frontend likely uses to build a human-readable or
purpose-scoped folder path. Preserve that folder/purpose prefix (the part before the filename) as
client-supplied and validated as today (no `..`, allow-listed content-type), but replace only the
**filename/suffix** portion with a server-generated GUID plus the original extension (derived from
`ContentType`, not trusted from a client-supplied filename string) — e.g.
`{tenant.StudioId}/{clientSuppliedFolderPrefix}/{Guid.NewGuid()}.{extensionForContentType}`. This
keeps the frontend's existing folder-organization behavior intact while removing the actual
overwrite/collision risk (the part that made the key unique). Confirm this shape against how R2
public URLs are consumed elsewhere (`PortfolioImage.ImageUrl`, `DesignRevision` image fields, etc.)
so the returned `publicUrl` still round-trips correctly through existing read paths.

### Backend

1. `GetPresignedUploadUrlHandler` — generate the suffix server-side per the design above.
2. `GetPresignedUploadUrlValidator` — the `ObjectKey` validation now applies only to the
   client-supplied folder-prefix portion; still block `..`, still cap length.
3. Confirm `PresignUploadResponse`'s `publicUrl` still correctly reflects the final, server-decided
   key (not the client's original `ObjectKey` guess) — the frontend must store whatever `publicUrl`
   comes back, not reconstruct its own.

### Frontend (the one narrow exception to this prompt's backend-only scope)

4. Every frontend call site of the presign endpoint (find them all — likely
   `frontend/src/features/designs/**`, `frontend/src/features/clients/**` for tattoo-record photos,
   `frontend/src/features/feedback/**` for the 2026-07-25 attachment feature, and any others) must
   use the returned `publicUrl` from the presign response as the source of truth for the uploaded
   file's final location, not reconstruct one from the request it sent. Read each call site fully
   before changing it — some may already do this correctly (in which case, no change needed there;
   state this explicitly per call site in the deliverable rather than changing code that doesn't
   need it).

### Tests

- Backend: two requests with an identical client-supplied folder prefix produce two different final
  object keys (proves the collision risk is closed).
- Backend: the returned `publicUrl`'s extension matches the validated `ContentType`.
- Frontend: each affected upload flow's component test confirms it persists/uses the server-returned
  `publicUrl`, not a client-reconstructed one.

### Help sync

**No Help Menu / user manual / onboarding-tour update needed** — this changes an internal storage
key format, not anything a user sees, fills in, or interacts with differently. State this
explicitly.

### Note on the CDN content-sniffing question (§2.2 item 3)

If `X-Content-Type-Options: nosniff` cannot be confirmed or set from within this repo, state that
plainly in the final summary as an infra follow-up for whoever manages the Cloudflare R2/CDN
configuration — do not attempt to fix this by adding application-layer code that can't actually
control CDN response headers.

---

## PHASE 7 — Dedicated rate limit for billing-mutation endpoints (Finding 7, Medium/P2)

### Current code

```csharp
// Pena_e_Arte.API/Endpoints/PaymentEndpoints.cs — no .RequireRateLimiting on any of these:
group.MapPost("/", CreatePaymentIntent).RequireAuthorization("OwnerOnly");
group.MapPost("/{id:guid}/capture", CaptureDeposit).RequireAuthorization("OwnerOnly");
group.MapPost("/{id:guid}/refund", RefundPayment).RequireAuthorization("OwnerOnly");
// Pena_e_Arte.API/Endpoints/BillingEndpoints.cs
billingGroup.MapPost("/subscription/checkout", CreateCheckout).RequireAuthorization("OwnerOnly");
```

### Target behavior

Add a new `"billing"` policy to `RateLimitingExtensions.cs` using the exact same `AddRedisPolicy`
helper already there — generous enough not to interfere with legitimate studio operations (this is
an abuse/cost-control backstop, not a UX-facing throttle), e.g. 20 requests/minute per user (key the
partition by authenticated user id via `ICurrentUser`/claims, not by IP, since these are
authenticated endpoints — confirm the correct claim to key on by checking how `ICurrentUser` is
populated elsewhere). Apply `.RequireRateLimiting("billing")` to `CreatePaymentIntent`,
`CreateDepositPayment`, `CaptureDeposit`, `RefundPayment`, `CreateCheckout`, and
`CreateCheckout/finalize`. Do not apply it to `DeclareCashDeposit`/`ConfirmCashDeposit` (no external
API call, no cost-abuse vector) or to read endpoints (`GetPayments`, `GetPaymentByAppointment`,
`GetClientSecret`, `DownloadInvoice`).

### Tests

- Exceeding the `billing` policy's limit on `CreatePaymentIntent` returns `429`.
- A legitimate sequence of normal owner operations (create a handful of payment intents in a test
  representative of real usage) stays under the limit and succeeds — regression test that the limit
  isn't so tight it breaks real usage.

### Help sync

No Help change needed — a backstop against abuse-level request volume, invisible to a legitimate
owner using the product normally.

---

## PHASE 8 — CORS production-misconfiguration guard (Finding 8, Low/P3)

### Current code

```csharp
// Pena_e_Arte.API/Extensions/CorsExtensions.cs
if (allowedOrigins.Length == 0)
    p.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
```

### Target behavior

Keep the existing fallback behavior unchanged (still needed for local dev with no configured
origins) but add a startup-time warning — or, if this codebase's conventions prefer failing loudly
in production over warning, a hard failure — when `IHostEnvironment.EnvironmentName` is
`"Production"` and `allowedOrigins` is empty. Check how other "don't silently misconfigure
production" guards in this codebase are implemented (e.g. `Jwt:SecretKey`'s docker-compose-level
`:?` pattern, or any existing `IHostEnvironment`-conditional check) before choosing warn-vs-throw,
and match the existing convention rather than introducing a third style.

### Tests

- `AddApiCors` with empty origins and `Environment = "Production"` triggers the warning/throw.
- `AddApiCors` with empty origins and `Environment = "Development"` behaves exactly as today
  (regression test — must not break local dev).

### Help sync

No Help change needed — startup configuration validation.

---

## Final self-check before declaring done

```
dotnet build   → 0 errors, 0 warnings
pnpm build     → 0 TypeScript errors (only relevant if Phase 6 touched frontend files)
dotnet test    → All green, including every new test listed above
pnpm test      → All green (only relevant if Phase 6 touched frontend files)
```

Plus, walk through each explicitly:

- Phase 1 (P0) is fully fixed in **all three** hubs (`ScheduleHub`, `DesignHub`, `NotificationHub`)
  — grep the final diff for `JoinStudio` and confirm no hub was missed.
- The Phase 1 regression test that proves a cross-tenant broadcast is no longer receivable actually
  exercises `NotifyStudioAsync` end-to-end, not just `JoinStudio`'s return value in isolation.
- Every §2.2 "verify, don't assume" item has an explicit, stated resolution in the final summary —
  not left ambiguous.
- Nothing under §3's "do not touch" list was modified except Phase 6's one narrowly-scoped frontend
  exception.
- No new `IgnoreQueryFilters()` call was added anywhere (none of these fixes should need one).
- No PII appears in any new log line added by these fixes.
- Every phase's Help-sync verdict ("no change needed") is stated explicitly in the deliverable per
  phase, not silently assumed.
- Confirm the fix for Finding 1 doesn't regress `SupportHub`'s own already-correct `JoinTicket`
  behavior (unrelated hub, but run its existing tests to be sure nothing shared was touched
  incorrectly).

---

## Final deliverable spec

**Files modified (backend):**
- `Pena_e_Arte.Infrastructure/Hubs/ScheduleHub.cs`, `DesignHub.cs`, `NotificationHub.cs` (Phase 1)
- `Pena_e_Arte.API/Program.cs` (Phase 2)
- `Pena_e_Arte.API/Endpoints/AuthEndpoints.cs` (Phase 3)
- `Pena_e_Arte.API/Extensions/HangfireDashboardAuthFilter.cs`,
  `tests/Pena_e_Arte.IntegrationTests/Infrastructure/HangfireDashboardAuthFilterTests.cs` (Phase 4)
- `Pena_e_Arte.API/Extensions/AuthenticationExtensions.cs` (Phase 5)
- `Pena_e_Arte.Application/Files/Queries/GetPresignedUploadUrlQuery.cs`,
  `Pena_e_Arte.Application/Files/Validators/GetPresignedUploadUrlValidator.cs` (Phase 6)
- `Pena_e_Arte.API/Extensions/RateLimitingExtensions.cs`,
  `Pena_e_Arte.API/Endpoints/PaymentEndpoints.cs`, `Pena_e_Arte.API/Endpoints/BillingEndpoints.cs`
  (Phase 7)
- `Pena_e_Arte.API/Extensions/CorsExtensions.cs` (Phase 8)

**Files potentially modified (frontend, Phase 6 only, exact list TBD by implementer after reading
live call sites):**
- Every component/hook that calls the presign endpoint and consumes its response.

**Files modified (config):**
- `docker-compose.yml`, `.env.example` (Phase 2's new optional `ForwardedHeaders:TrustedProxyCidr`;
  Phase 4's resolution, whichever direction it takes)

**Docs:**
- `docs/claude/architecture.md` — new Decisions Log entry:

  > **Security remediation (adversarial pass findings)** — 2026-07-26. Fixed the P0 cross-tenant
  > SignalR authorization gap in `ScheduleHub`/`DesignHub`/`NotificationHub` (mirrors `SupportHub`'s
  > 2026-07-21 `JoinTicket` fix — the same defect existed in all three studio-scoped hubs and was
  > never generalized past the one hub that got reviewed). Also: forwarded-header trust
  > restriction, rate limiting added to `reset-password`/`refresh`/`verify-email`, Hangfire
  > dashboard auth resolved ([fill in: Basic Auth wired / dead config removed — state which]), a
  > startup-time JWT signing-key strength guard, server-generated R2 upload object-key suffixes, a
  > new `billing` rate-limit policy on Stripe-calling endpoints, and a CORS production-misconfig
  > guard. Full findings and evidence in `docs/claude/security-audit-adversarial-2026-07-26.md`.
  > [Fill in: resolution of each §2.2 "verify" item — Hangfire browser reachability, K3s ingress
  > topology, R2/CDN nosniff header.]

**Commit message:**
```
fix(security): close cross-tenant SignalR authorization gap and harden auth/CORS/rate-limiting

- ScheduleHub/DesignHub/NotificationHub.JoinStudio now validates tenant membership (P0)
- Restrict trusted forwarded-header proxies for accurate rate-limit partitioning
- Rate-limit reset-password/refresh/verify-email
- Resolve Hangfire dashboard auth (dead Basic-Auth config vs actual JWT role check)
- Fail-fast startup guard on JWT signing-key strength
- Server-generate R2 presigned-upload object-key suffixes
- Add dedicated rate limit for Stripe-calling billing endpoints
- Fail loud (not open) on missing CORS origins in production

See docs/claude/security-audit-adversarial-2026-07-26.md for full findings.
```
