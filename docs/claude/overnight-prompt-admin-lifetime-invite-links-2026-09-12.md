# Overnight Master Prompt — Admin-Issued Lifetime Access Invite Links

**Date:** 2026-09-12
**Requester:** Phi (platform owner), via the Pena e Artë Engineering Consultation project
**Origin:** Consultation-project feature spec, engineered for unattended execution by a
Claude Code session with full repo write access in the main **Pena e Artë - Engineering**
project. This document was produced by the *separate, narrower* Engineering Consultation
project (read-only against the codebase) — it does not implement anything itself.
**Mode:** Fully autonomous. No user present during execution. Do not pause for
clarification on anything this document has already decided (§2) — only stop for the
items explicitly listed in §9 ("do not build blind").

---

## 0. Before you write a single line — checkpoint and branch

```bash
git add -A
git commit -m "checkpoint: before admin lifetime invite links work" --allow-empty
git checkout -b feature/admin-lifetime-invites
```

If the working tree is already clean, the `--allow-empty` commit still gives you a named
rollback point. Do not skip this even though it feels redundant on a clean tree — it is
the one thing standing between "this pass went sideways" and "we lost nothing."

---

## 1. What this feature is

A platform admin (role `admin`, policy `AdminOnly` — renamed from `issuer` on
2026-09-06, migration `RenameIssuerRoleToAdmin`, confirmed live in
`Pena_e_Arte.API/Extensions/AuthorizationExtensions.cs:14`) can generate a **time-boxed,
single-use invite link** that grants the redeemer a studio permanently on a new
**Lifetime** plan tier — full current *and* future feature access, at $0, forever, no
Stripe billing ever attached. This is a sales/growth tool for onboarding specific
high-value prospects (a studio owner or an independent artist not yet registered) with an
irresistible, time-pressured offer — conceptually the SaaS "founder lifetime deal" /
AppSumo-style LTD pattern, aimed at a named or general prospect rather than the public.

Two invite targets, two different provisioning outcomes (this codebase has no artist
account that exists independent of a Studio — see §3.3):

- **Owner invite** → redeemer goes through the existing studio registration flow
  (`RegisterStudioCommand`), but the resulting `Subscription` is created directly on the
  Lifetime plan instead of the normal 14-day-trial path.
- **Artist invite** → redeemer goes through the existing **solo artist** signup flow
  (`RegisterSoloArtistCommand`, the same one `Studio.IsSolo` self-serve signups use),
  which already auto-provisions a `Studio` + `Subscription` for an independent artist —
  except the auto-provisioned `Subscription` lands on Lifetime instead of Free.

The redeemer never sees a pricing page, never enters a card, and is never asked to pick a
plan. The link itself carries a real, server-enforced expiry shown as a countdown on the
landing page before they start the form.

---

## 2. Decisions already made — do not re-litigate these

These were confirmed directly with the product owner before this document was written.
Build to them; do not "improve" on them without flagging why in the Decisions Log entry
(§13).

1. **Global seat cap, admin-configurable.** The platform enforces a hard, live-tracked
   cap on the total number of Lifetime seats ever granted (across both owner and artist
   invites combined, one shared counter — see §7.1 for why combined, not per-role). An
   admin sets this cap (a single platform-wide setting, not per-invite) and the admin UI
   shows "X of Y lifetime seats claimed" at all times. Once the cap is reached, invite
   *generation* is blocked with a clear message — **already-generated, unredeemed invites
   already in someone's inbox remain redeemable** even if the cap fills up in the
   meantime from other invites (an admin closing out the campaign should *revoke*
   outstanding invites explicitly, not have them silently start failing on strangers).
   This is also what makes the "very limited offer" landing-page copy legally honest
   scarcity rather than a fabricated-urgency dark pattern — see §8.4.
2. **"Full lifetime access, including future features" is the literal build target, with
   a mandatory ongoing process attached.** This is *not* time-boxed or feature-frozen at
   grant date. The Lifetime `Plan` row is built today with every limit unlimited and
   every perk flag on. Going forward, **every future overnight prompt that adds a new
   plan-gated dimension (a new `Max*` field on `Plan`, a new boolean feature flag, or any
   new gating mechanism that isn't a `Plan` field at all) must explicitly set that new
   gate to its most-generous value on the seeded Lifetime plan row as part of that same
   change** — this is now a standing rule, added to `CLAUDE.md`/`architecture.md` in this
   pass (§13.3). This is a real, accepted, open-ended business-risk commitment — flagged
   again in §9.1, not silently absorbed.
3. **Artist invites auto-provision a solo studio on the Lifetime plan.** Reuses
   `RegisterSoloArtistCommand`'s existing auto-provisioning path verbatim, with the plan
   swapped. No new "artist without a studio" concept is introduced.
4. **Email lock is optional, per invite.** An admin may attach a target email when
   generating a link. If set, redemption's registration email must case-insensitively
   match it or the redemption is rejected (clear error, not a silent fallback — see
   §8.4). If left blank, the link is a general shareable single-use invite — first
   redemption wins, no identity check beyond the single-use guarantee itself.

---

## 3. Verified current state (read from live source, 2026-09-12 — re-confirm anything
this session's own edits will touch, since this file can lag by the time you run it)

### 3.1 Plan / PlanPrice / Subscription (`Pena_e_Arte.Domain/Entities/`)

`Plan.cs` — every quota is nullable-means-unlimited; four boolean perk flags default
false:

```csharp
public int? MaxArtists { get; set; }
public int? MaxAppointmentsPerMonth { get; set; }
public int? MaxNotificationsPerMonth { get; set; }
public int? MaxStorageGb { get; set; }
public int? MaxLocations { get; set; }
public bool AllowApiAccess { get; set; } = false;
public bool PrioritySupport { get; set; } = false;
public bool AllowMarketingCampaigns { get; set; } = false;
public bool AllowBrandingRemoval { get; set; } = false;
```

`Plan` has **no** field today that hides a tier from public plan-listing/pricing
surfaces (`PlanManagementPage`, `SubscribePage`, `plan-tiers.html`) — every seeded Plan is
implicitly public. This must change (§4.1) — the Lifetime plan must never appear as a
purchasable option to a normal owner.

`Subscription.cs` — `StripeSubscriptionId` is already nullable (Free-plan studios have
it null today), `CurrentPeriodEnd`/`GracePeriodEnd` are plain `DateTime` with an
established **"never expires" sentinel convention**: `DateTime.UtcNow.AddYears(50)`,
used verbatim in both `RegisterSoloArtistHandler` (Free plan, solo signup) and
`CreateSubscriptionHandler` (Free plan chosen at checkout). Reuse this exact sentinel for
Lifetime — do not invent a new one (e.g. `DateTime.MaxValue`, which several `DateTime`
serialization paths in this codebase are not guaranteed to round-trip cleanly, per the
existing convention already avoiding it everywhere else).

### 3.2 The registration handlers this feature must hook into

**`RegisterSoloArtistHandler`** (`Pena_e_Arte.Application/Auth/Commands/RegisterSoloArtistCommand.cs`)
today, verbatim:

```csharp
Plan freePlan = await db.Plans.FirstOrDefaultAsync(p => p.Name == "Free", ct)
    ?? throw new InvalidOperationException("Free plan not seeded — DataSeeder must run first.");

Subscription subscription = new()
{
    StudioId = studio.Id,
    PlanId = freePlan.Id,
    BillingInterval = BillingInterval.Monthly,
    Status = SubscriptionStatus.Active,
    TrialExpiresAt = null,
    CurrentPeriodEnd = DateTime.UtcNow.AddYears(50),
    GracePeriodEnd = DateTime.UtcNow.AddYears(50),
};
```

Identity user is created **before** `db.Studios.Add`/`db.Subscriptions.Add` (if
`CreateUserAsync` fails, nothing has been written yet — preserve this ordering). Email
verification is sent in a non-blocking `try/catch` after `SaveChangesAsync`, logging only
`userId`/`studioId` on failure — never the email address (Rule #3).

**`RegisterStudioHandler`** (`Pena_e_Arte.Application/Studios/Commands/RegisterStudioCommand.cs`)
today resolves an *optional* `req.ReferralCode` into a `PendingReferralCodeId` stored on
`Studio` — but does **not** fully redeem it there; redemption (Stripe coupon,
`ReferralRedemption` row) happens later, in `CreateSubscriptionHandler`, when the trial
converts to a real paid plan at checkout. `Subscription` here is created `Trialing`, 14
days, 7-day grace (`trialEnd.AddDays(7)`).

This is the single most important divergence Lifetime invites must make from the
existing referral-code pattern, and it must be **stated explicitly in the Decisions Log
entry** (§13.3), not silently done differently: a Lifetime invite has no later
checkout/plan-selection step to redeem against — the whole point is the redeemer never
sees one. **Lifetime-code validation AND full redemption both happen inside
`RegisterStudioHandler`/`RegisterSoloArtistHandler` themselves, in the same
`SaveChangesAsync` transaction that creates the Studio** — `Subscription.Status` is
`Active` from the first row ever written, `TrialExpiresAt` is `null`, never `Trialing`.

`RegisterStudioRequest` today (`Pena_e_Arte.Contracts/Requests/RegisterStudioRequest.cs`):

```csharp
public record RegisterStudioRequest(
    string Name, string Slug, string City, double Latitude, double Longitude,
    string OwnerEmail, string Nipt, string? ReferralCode = null);
```

`RegisterSoloArtistRequest` today:

```csharp
public record RegisterSoloArtistRequest(
    string Email, string Password, string FirstName, string LastName);
```

**Re-verify before editing:** this session's own exploration could not fully re-confirm
the exact wiring between `RegisterStudioRequest` (studio creation) and the separate
Identity-user-creation call in the owner registration flow (`AuthEndpoints.cs`/
`StudioEndpoints.cs` — the two-step registration architecture.md's Decisions Log
describes for OAuth: "`referralCode` is included in the step-1 studio-creation payload").
**Read `Pena_e_Arte.API/Endpoints/StudioEndpoints.cs` and `AuthEndpoints.cs` and the
frontend `RegisterPage`/`StudioRegistrationPage` flow start-to-finish before writing any
code that extends this request shape** — do not assume this document's description of
the multi-step flow is complete; it is a best-effort reconstruction from `grep`, not a
full read of those files.

### 3.3 Why "artist invite" means "auto-provision a solo studio," not "join an existing
studio"

`Artist` (`Pena_e_Arte.Domain/Entities/Artist.cs`) is a `TenantEntity` — it always
belongs to exactly one `Studio` (tenant) and has no billing/plan concept of its own; only
`Studio.Subscription` carries a `Plan`. There is no such thing as "an artist account with
lifetime access" independent of some studio's subscription. The only existing flow that
creates a `Studio` *for* an artist with no pre-existing studio is
`RegisterSoloArtistCommand` (`Studio.IsSolo = true`). Reusing it is not a workaround —
it is the only shape in this codebase that matches what an admin-invited, not-yet-onboarded
artist actually needs.

### 3.4 Existing invite/admin-generation precedents to copy conventions from, not to
extend directly

- **`StudioJoinInvite`** (`Pena_e_Arte.Domain/Entities/StudioJoinInvite.cs`) — the
  precedent for "an invite entity that is deliberately **not** a `TenantEntity`" (the
  invited party isn't a tenant member until they accept), with its own
  `Status`/`ExpiresAt`/`RespondedAt` shape. Copy this shape's *reasoning*, not its fields
  — it's studio-to-artist, ours is admin-to-anyone-not-yet-registered.
- **`ReferralCode`/`ReferralRedemption`** + `AdminGenerateReferralCodeCommand`
  (`Pena_e_Arte.Application/Platform/Commands/AdminGenerateReferralCodeCommand.cs`) — the
  precedent for "admin generates a redeemable code for someone who doesn't have an
  account yet," including the exact crypto-random code generator to reuse (see §4.3) and
  the `IgnoreQueryFilters()` admin-cross-tenant-read pattern. **Do not extend
  `ReferralCode` itself** — it is structurally tied to an *existing* `StudioId` (a studio
  referring another studio) and to Stripe-coupon economics; Lifetime invites have neither.
  A new, separate entity is correct here, same reasoning `architecture.md`'s Decisions Log
  already used to reject reusing `IntakeForm` for `BookingIntake` and to reuse — not bolt
  onto — `DepositRule`'s idiom for `PromoCode`.
- **`AuditLogEntry`/`IAuditableCommand`/`AuditActions`
  (`Pena_e_Arte.Domain/Constants/AuditActions.cs`)** — every admin action that touches
  money or account-level access already goes through this. A command granting *permanent,
  irrevocable-in-practice, zero-cost, all-future-features* platform access is about as
  sensitive an admin action as exists in this codebase — it gets full audit coverage,
  full stop.
- **`IgnoreQueryFilters()` approved-usage count is currently at 51** (guest-checkout
  booking entry, `docs/claude/architecture.md` Decisions Log, 2026-08-31 — **re-count
  from the live `architecture.md` before numbering**, this file may have moved since
  2026-09-12). The new admin cross-tenant reads this feature needs (list/lookup a
  `LifetimeInvite` regardless of who redeemed it) is the next approved usage — number it
  accordingly in both the "IgnoreQueryFilters Approved Usages" table and inline code
  comments; do not invent a number without re-checking.
- **User manual: `frontend/public/user-manual/index.html` is the live, actively
  maintained copy** (340KB, last touched by real feature commits — POK payment provider,
  webhooks, API access). `docs/user-manual.html` is stale (106KB, last touched by a
  rebrand commit in July) — **do not update the stale copy and call Help-sync done.**
  Verify this is still true at build time (`git log -3 -- <path>` for both) before
  trusting this document's claim.
- **Onboarding tours are `frontend/src/features/help/tours/{client,artist,owner,admin}Tour.ts`**
  — confirmed renamed from `issuerTour.ts` to `adminTour.ts` already (consistent with the
  September 6 role rename). Do not reintroduce `issuer` naming anywhere in new code.
- **Rate-limit policy names already exist and are reused across public endpoints**:
  `public-read`, `public-write`, `public-booking`, `billing` (see the `AllowAnonymous
  Exceptions` table in `architecture.md`). The closest precedent to our new public
  preview endpoint is `GET /api/v1/gift-cards/{code}/balance` — a public, code-only,
  enumeration-risk lookup, accepted at `public-read` with the enumeration risk explicitly
  documented rather than solved. Do the same here rather than inventing a new policy.

---

## 4. Data model

### 4.1 `Plan` — add one field

```csharp
/// <summary>False for internal-only tiers (e.g. the Lifetime plan) that must never
/// appear on SubscribePage, PlanManagementPage's studio-facing picker, or plan-tiers.html
/// — assignable only through a dedicated admin-only path. Default true preserves every
/// existing seeded Plan's current visibility with zero migration-time data change beyond
/// the new column's default.</summary>
public bool IsPubliclyPurchasable { get; set; } = true;
```

Audit every current read site that lists Plans for a studio-facing picker (`SubscribePage`
query, `PlanManagementPage`'s studio-facing endpoints, `plan-tiers.html`'s data source —
confirm whether that HTML file is static/hand-maintained or served from an endpoint) and
add `.Where(p => p.IsPubliclyPurchasable)`. Do **not** filter it out of the *admin's own*
`PlanManagementPage` editor view — admins must still be able to see/edit the Lifetime
plan's limits there (this is in fact how the "keep it generous as new features ship"
process in §2.2/§13.3 gets executed in practice).

### 4.2 `Subscription` — add one field

```csharp
/// <summary>True only for a Subscription created by redeeming a LifetimeInvite. Never
/// set any other way — mirrors Studio.IsSolo's "never set any other way" convention.
/// Defense-in-depth marker, independent of PlanId: (1) excludes this subscription from
/// PastDueReminderJob/dunning sweeps and any future recurring-billing job even if a bug
/// elsewhere ever left StripeSubscriptionId non-null on it; (2) lets SubscriptionOversightPage
/// and the admin's own billing tooling flag it distinctly so a human doesn't accidentally
/// "fix" it during a billing sweep; (3) blocks self-serve plan changes away from Lifetime
/// through the ordinary CreateSubscriptionCommand/checkout path (§4.4) — an owner should
/// not be able to silently downgrade their own lifetime grant by clicking "change plan.")
/// </summary>
public bool IsLifetimeGrant { get; set; } = false;
```

### 4.3 New entity — `LifetimeInvite`

`Pena_e_Arte.Domain/Entities/LifetimeInvite.cs`:

```csharp
namespace Pena_e_Arte.Domain.Entities;

public enum LifetimeInviteTargetRole { Owner, Artist }
public enum LifetimeInviteStatus { Pending, Redeemed, Revoked, Expired }

/// <summary>
/// An admin-generated, time-boxed, single-use invite that grants the redeemer's studio a
/// permanent, unbilled Lifetime-plan Subscription. Deliberately NOT a TenantEntity — like
/// StudioJoinInvite, the redeemer has no tenant membership (often no account at all) until
/// they accept, so this must be readable/writable with no ambient ICurrentTenant. See
/// AppDbContext's "Admin-level (no tenant filter)" DbSet section.
/// Status is derived to Expired lazily at read/redeem time from ExpiresAt — there is no
/// background job flipping Pending -> Expired (mirrors how ReferralCode/StudioJoinInvite
/// both check ExpiresAt inline rather than running a sweep job for a field that is only
/// ever consulted, never displayed as a list-filterable "current" status independent of
/// a live check — confirm this against how StudioJoinInviteStatus.Expired is actually
/// populated today before committing to "no job," since this file did not verify that
/// mechanism's implementation, only the entity's shape).
/// </summary>
public class LifetimeInvite
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Cryptographically random, unguessable — this grants far more value than a
    /// discount-month ReferralCode, so it is longer than AdminGenerateReferralCodeHandler's
    /// 8-char alphabet code. See §4.3's code-generation note below for the exact length/charset
    /// decision and why.</summary>
    public string Code { get; set; } = string.Empty;

    public LifetimeInviteTargetRole TargetRole { get; set; }

    /// <summary>Optional email lock (Decision §2.4). Null = open/shareable, first redemption
    /// wins. Set = redemption's registration email must case-insensitively match this exactly,
    /// or the redemption is rejected with a distinct error (§8.4) — never silently ignored.</summary>
    public string? InvitedEmail { get; set; }

    public LifetimeInviteStatus Status { get; set; } = LifetimeInviteStatus.Pending;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }
    public Guid CreatedByAdminUserId { get; set; }

    public DateTime? RedeemedAt { get; set; }
    /// <summary>The Studio created/provisioned by redemption — null until Redeemed.</summary>
    public Guid? RedeemedStudioId { get; set; }

    public DateTime? RevokedAt { get; set; }
    public Guid? RevokedByAdminUserId { get; set; }
}
```

EF configuration (`Pena_e_Arte.Infrastructure/Persistence/Configurations/LifetimeInviteConfiguration.cs`,
mirroring `StudioJoinInviteConfiguration.cs`'s shape): unique index on `Code`; plain
(non-unique) index on `InvitedEmail` and on `Status` for the admin list/filter query;
`TargetRole`/`Status` stored as strings via the same enum-to-string convention the rest of
this codebase uses (`SubscriptionStatus`, `StudioJoinInviteStatus`) — **verify the actual
convention (native enum column vs. `HasConversion<string>()`) against
`StudioJoinInviteConfiguration.cs` directly rather than assuming.**

**Code generation:** extract `AdminGenerateReferralCodeHandler.GenerateCode`'s
crypto-random-alphabet approach into a small shared utility (e.g.
`Pena_e_Arte.Domain.Utilities.SecureCodeGenerator.Generate(int length, string alphabet)`)
rather than duplicating the `RandomNumberGenerator.GetBytes` loop a second time, and call
it with a longer length for `LifetimeInvite` — 16 characters minimum against the same
36-character alphabet (`A–Z0–9`), giving ~82 bits of entropy versus the referral code's
~41 bits, appropriate to what this code is worth. **Before extracting**, confirm
`AdminGenerateReferralCodeHandler.GenerateCode`'s existing unit tests don't assert on the
method's exact location/visibility (`internal static`) in a way the extraction would
break — check `ReferralCode`-suite tests before moving it, and prefer leaving the
existing method as a thin wrapper calling the new shared utility rather than deleting it,
to keep the diff there at zero behavioral risk.

### 4.4 `Studio` — no schema change; one new guard

No new field needed on `Studio` — `IsLifetimeGrant` lives on `Subscription`, which
already has a 1:1 `Studio`. Add a guard in `CreateSubscriptionHandler`
(`Pena_e_Arte.Application/Billing/Commands/CreateSubscriptionCommand.cs`): if
`subscription.IsLifetimeGrant` is true, throw `BusinessRuleViolationException("This
studio has a permanent Lifetime plan and cannot change plans through checkout. Contact
the platform to make changes.")` before any Stripe customer/subscription creation call —
this is the enforcement half of the defense-in-depth comment on `IsLifetimeGrant` itself
(§4.2, point 3).

### 4.5 Seed the Lifetime `Plan` row

Locate the existing Plan-seeding block (`DataSeeder` — this session's own search for the
exact file path/line timed out; find it with `grep -rn "\"Free\"" --include=*.cs
Pena_e_Arte.Infrastructure` scoped to a specific likely directory rather than the whole
tree, or open the seeder file directly once located via `find`). Add, once, guarded by
"does a Plan named `Lifetime` already exist" the same way the Free plan lookup already
guards against re-seeding:

```csharp
Plan lifetimePlan = new()
{
    Name = "Lifetime",
    IsPubliclyPurchasable = false,
    MaxArtists = null,
    MaxAppointmentsPerMonth = null,
    MaxNotificationsPerMonth = null,
    MaxStorageGb = null,
    MaxLocations = null,
    AllowApiAccess = true,
    PrioritySupport = true,
    AllowMarketingCampaigns = true,
    AllowBrandingRemoval = true,
};
```

No `PlanPrice` rows at all for this plan (it is never purchased at any interval — the
absence of any row is what makes it invisible to `SubscribePage`'s interval picker even
before the `IsPubliclyPurchasable` filter is applied, belt-and-suspenders).

---

## 5. Backend — commands, queries, endpoints

### 5.1 `Pena_e_Arte.Application/Platform/Commands/CreateLifetimeInviteCommand.cs`

```csharp
public record CreateLifetimeInviteCommand(
    LifetimeInviteTargetRole TargetRole,
    string? InvitedEmail,
    DateTime ExpiresAt) : IRequest<LifetimeInviteResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.LifetimeInviteCreated;
    public string AuditTargetType => AuditTargetTypes.LifetimeInvite;
    public Guid AuditTargetId { get; init; } // set post-creation — check how other
        // Create-shaped IAuditableCommands that don't know their target id at construction
        // time solve this (AuditLogBehavior's doc comment implies it logs AFTER the handler
        // completes — confirm whether AuditTargetId is read before or after Handle() runs
        // before assuming this pattern compiles/behaves as written; this is a real open
        // question this session could not fully resolve from the interface alone — read
        // AuditLogBehavior.cs itself, not just IAuditableCommand.cs, before implementing.)
}
```

Handler responsibilities, `AdminOnly`:

1. Validate `ExpiresAt` is in the future and not absurdly far out (FluentValidation: must
   be between now+1 hour and now+30 days — an admin fat-fingering "expires in 3000 hours"
   should get a validation error, not a multi-year-lived high-value credential).
2. Check the global seat cap (§6 — new `PlatformSettings`-style single row or reuse
   whatever this codebase's existing pattern is for a single platform-wide tunable value;
   **search for how `AllowMarketingCampaigns`-adjacent platform-wide toggles, or the
   referral-reward-size, are configured today — this codebase may already have a
   `PlatformSettings` singleton table or may use `IConfiguration`/appsettings for
   platform-wide non-per-studio values; do not invent a second mechanism if one exists**).
   Count = `LifetimeInvite` rows with `Status == Redeemed`. If count >= cap, throw
   `BusinessRuleViolationException("The lifetime seat cap has been reached.")` — do not
   silently let generation succeed past the cap (Decision §2.1 is explicit that generation
   is what gets blocked, not redemption of already-issued invites).
3. Generate a unique `Code` (§4.3), retry-on-collision loop mirroring
   `AdminGenerateReferralCodeHandler.GenerateUniqueCodeAsync`'s 10-attempt shape.
4. Persist, return `LifetimeInviteResponse` including the shareable URL:
   `$"{appSettings.BaseUrl}/lifetime/{invite.Code}"` (mirrors
   `AdminGenerateReferralCodeHandler`'s `shareUrl` construction, but note that referral
   codes there hardcode `https://tattooos.co/register?ref=` literally instead of using
   `IAppSettings.BaseUrl` — **do not copy that hardcoding; use `IAppSettings.BaseUrl` here,
   and flag the referral code's hardcoded domain as a pre-existing, unrelated smell in
   your final summary rather than fixing it in this pass** — out of scope, don't scope-creep).

### 5.2 `Pena_e_Arte.Application/Platform/Commands/RevokeLifetimeInviteCommand.cs`

`AdminOnly`, `IAuditableCommand`. Sets `Status = Revoked`, `RevokedAt`,
`RevokedByAdminUserId`. Throws if already `Redeemed` (`BusinessRuleViolationException` —
an admin cannot revoke a seat someone already claimed; that requires a support/manual
process, not this endpoint, and is explicitly out of scope — see §9).

### 5.3 `Pena_e_Arte.Application/Platform/Queries/GetLifetimeInvitesQuery.cs`

`AdminOnly`. Cross-tenant list, `IgnoreQueryFilters()` — **the next approved usage after
#51, re-count and number correctly** (§3.4). Returns every invite with derived
`Status` (flip `Pending` → `Expired` in the response mapping when `ExpiresAt` has passed,
without mutating the stored row — same "derive at read time" posture as the entity's own
doc comment, §4.3), plus `RedeemedStudioId`/`RedeemedStudio.Name` for admin audit context
(joined via the same `IgnoreQueryFilters()` cross-tenant `Studios` read
`AdminGenerateReferralCodeHandler` already uses).

### 5.4 `Pena_e_Arte.Application/Platform/Queries/GetLifetimeInvitePreviewQuery.cs`

**Public** (no `[Authorize]`, `AllowAnonymous`) — this is the one query the unauthenticated
landing page needs. `IgnoreQueryFilters()` (next approved usage after 5.3's). Given a
`Code`:

- Not found / wrong code → a distinct "not found" result (frontend renders "This invite
  link isn't valid").
- Found but `Revoked` → distinct result ("This offer is no longer available").
- Found but past `ExpiresAt` (and still `Pending`) → distinct result ("This offer has
  expired").
- Found but already `Redeemed` → distinct result ("This seat has already been claimed").
- Found, `Pending`, not expired → the real preview:

```csharp
public sealed record LifetimeInvitePreviewResponse(
    LifetimeInviteTargetRole TargetRole,
    DateTime ExpiresAt,
    bool IsEmailLocked, // true/false only — NEVER the actual InvitedEmail value
    string Status // "Valid" | "Expired" | "Redeemed" | "Revoked" | "NotFound"
);
```

**Do not return `InvitedEmail` in this response, in any form, even partially masked.**
This is a public, unauthenticated, code-guessable-adjacent endpoint (`public-read` rate
limit, same enumeration-risk acceptance as the gift-card-balance precedent, §3.4) — an
email address is PII (Rule #3's spirit extends past logs to any anonymous-readable
response) and the intended recipient does not need it echoed back to know their own
address. `IsEmailLocked` alone is enough for the frontend to show "this offer is reserved
for a specific person" copy without leaking who. The actual match happens server-side, at
redemption, inside `RegisterStudioHandler`/`RegisterSoloArtistHandler` (§5.5) — a
non-matching email fails at submit time with a clear inline error, not proactively.

Route: `GET /api/v1/lifetime-invites/{code}`, rate-limited `public-read`. **New row
required in the `AllowAnonymous Exceptions` table** (§13.1) — this is a hard CLAUDE.md
Rule #2 requirement, not optional paperwork.

### 5.5 Redemption — inside the existing registration handlers, not a separate endpoint

Add an optional `string? LifetimeInviteCode` to both `RegisterStudioRequest` (trailing,
after the existing `ReferralCode`) and `RegisterSoloArtistRequest` (trailing, new last
param) — additive, matches this codebase's own established "trailing positional-record
parameters with defaults" convention (already used repeatedly per `architecture.md`'s
Decisions Log, e.g. the `PromoCode` P1-backlog entry). **Grep every existing construction
call site of both records (including every test) before relying on this being safe** — the
same verification the `PromoCode` entry explicitly did.

In each handler, **before** building the `Subscription`:

```csharp
LifetimeInvite? invite = null;
if (!string.IsNullOrWhiteSpace(req.LifetimeInviteCode))
{
    invite = await db.LifetimeInvites.IgnoreQueryFilters()
        .FirstOrDefaultAsync(i => i.Code == req.LifetimeInviteCode, ct);

    if (invite is null)
        throw new BusinessRuleViolationException("This invite link is not valid.");
    if (invite.Status == LifetimeInviteStatus.Revoked)
        throw new BusinessRuleViolationException("This invite is no longer available.");
    if (invite.Status == LifetimeInviteStatus.Redeemed)
        throw new BusinessRuleViolationException("This invite has already been used.");
    if (invite.ExpiresAt < DateTime.UtcNow)
        throw new BusinessRuleViolationException("This invite has expired.");
    if (invite.TargetRole != /* Owner for RegisterStudioHandler, Artist for
        RegisterSoloArtistHandler */)
        throw new BusinessRuleViolationException("This invite is not valid for this
            registration type.");
    if (invite.InvitedEmail is not null &&
        !string.Equals(invite.InvitedEmail, req.OwnerEmail /* or req.Email */,
            StringComparison.OrdinalIgnoreCase))
        throw new BusinessRuleViolationException(
            "This invite was reserved for a different email address.");
}
```

**This must hard-fail the entire registration, not silently fall back to a normal
Free/Trial signup.** This is a deliberate divergence from the `PromoCode` precedent's
"an unresolved code is silently ignored, a guest fat-fingering it must never be blocked"
posture (§3.2/architecture.md P1-Group-4 entry) — that precedent is for a discount at
checkout, low stakes, recoverable. This is someone who arrived specifically because they
were promised a permanent lifetime seat; silently registering them on a normal trial
instead, with no error, is a straightforwardly deceptive outcome, not a graceful
degradation. **State this divergence explicitly, with this reasoning, in the Decisions
Log entry (§13.3)** — do not let it read as an unexplained inconsistency next to the
PromoCode entry.

Then, if `invite is not null` and every check passed, build `Subscription` on the
Lifetime plan instead of Free/Trialing:

```csharp
Plan lifetimePlan = await db.Plans.FirstOrDefaultAsync(p => p.Name == "Lifetime", ct)
    ?? throw new InvalidOperationException("Lifetime plan not seeded — DataSeeder must run first.");

Subscription subscription = new()
{
    StudioId = studio.Id,
    PlanId = lifetimePlan.Id,
    BillingInterval = BillingInterval.Monthly,
    Status = SubscriptionStatus.Active,
    TrialExpiresAt = null,
    CurrentPeriodEnd = DateTime.UtcNow.AddYears(50),   // same sentinel, §3.1
    GracePeriodEnd = DateTime.UtcNow.AddYears(50),
    IsLifetimeGrant = true,
};

invite.Status = LifetimeInviteStatus.Redeemed;
invite.RedeemedAt = DateTime.UtcNow;
invite.RedeemedStudioId = studio.Id;
```

— in the *same* `SaveChangesAsync` call that persists `Studio`. `RegisterStudioHandler`
specifically also currently sets `Studio.TrialExpiresAt = trialEnd` and schedules three
Hangfire jobs (`ScheduleTrialExpiryWarning`/`ScheduleTrialExpiry`/`ScheduleGracePeriodEnd`)
unconditionally — **these three job schedules must be skipped when a Lifetime invite was
redeemed** (there is no trial to warn about or expire); guard them behind
`invite is null`, and set `Studio.TrialExpiresAt` to the same 50-year sentinel for
consistency with `Subscription`'s fields rather than leaving it at a real near-term date
that nothing will ever act on.

**Race condition to close, and to write a real concurrency test for**: two people
completing registration with the same single-use code at nearly the same instant must not
both succeed. The cheapest correct fix consistent with this codebase's existing
concurrency posture (`Studio.Slug` uniqueness is enforced by a real unique index, not just
an application-layer check, per `RegisterStudioHandler`'s own slug-suffix loop existing
*because* the index is real) is a unique index or unique-constraint-shaped guard on
`LifetimeInvite` that makes a second concurrent `UPDATE ... SET Status = 'Redeemed' WHERE
Id = @id AND Status = 'Pending'`-shaped write fail/return zero-rows-affected for the
loser, not two successful `SaveChangesAsync` calls both marking it Redeemed. **Write
`LifetimeInviteRedemptionRaceIntegrationTests` against a real MySQL context, modeled
directly on how `AcceptStudioJoinInviteIntegrationTests` proved its own account-swap
race is closed** — this is the single highest-value test in this entire feature, because
the failure mode (two studios both believe they got the one lifetime seat, one of them
paying nothing forever by a bug rather than by design) is exactly the kind of thing that
is expensive to discover after the fact.

### 5.6 Email confirmation

`Pena_e_Arte.Infrastructure/Services/MailKit/EmailRenderer.cs` gains
`RenderLifetimeInviteConfirmation(string recipientName)` (or whatever the existing
`RenderArtistInvite`/`RenderEmailVerification` methods' parameter shape convention is —
match it, don't invent a new signature style). Sent non-blocking, `try/catch`, logging
only `userId`/`studioId` on failure (never the email address), from inside
`RegisterStudioHandler`/`RegisterSoloArtistHandler` right after the existing
email-verification send — **not** a separate job, since this is a one-time, low-volume,
already-in-a-non-blocking-try-catch send exactly like the email it sits next to.

Subject/body copy: confirm the lifetime grant explicitly, state it's permanent, and
close with "we'll be in touch" — this is the email the success page (§8) promises is
coming, so its content must actually match that promise.

### 5.7 `AllowAnonymous Exceptions` table entry (new row required)

```
| `GET /api/v1/lifetime-invites/{code}` | Public invite-preview landing page, no JWT possible before an account exists | Rate-limited (`public-read`); response never includes InvitedEmail, only IsEmailLocked boolean; code is 16-char crypto-random (~82 bits), enumeration risk accepted at that rate limit, same posture as `GET /api/v1/gift-cards/{code}/balance` |
```

---

## 6. Global seat cap — where the number lives

This session could not confirm whether this codebase already has a
"single platform-wide tunable setting" mechanism (a `PlatformSettings` singleton table,
an admin-editable config row, or plain `appsettings`/environment-variable configuration
for platform-wide non-per-studio values). **Search for one before building a new one** —
grep for how the referral-reward size ("one month free," called out in
`architecture.md`'s Decisions Log as a `// TODO(product)` in `ReferralRewardService`, not
yet configurable) or any other single admin-tunable number is currently stored. If
genuinely nothing exists:

- Simplest correct option: a `PlatformSettings` table with exactly one row (`Id` fixed/
  singleton pattern, or just the first-and-only row by convention — check if this
  codebase already has a "singleton row" pattern anywhere before inventing the idiom),
  holding `LifetimeSeatCap` (`int`), admin-editable from the same page that generates
  invites. This is the recommended default absent a discovered existing mechanism.
- Do **not** hardcode the cap in `appsettings.json` — Decision §2.1 requires it be
  admin-configurable at runtime without a deploy, and the admin UI needs to both read and
  write it live.

---

## 7. Frontend

### 7.1 Admin — "Lifetime Invites" page

New route under the admin/platform area, alongside `PlatformReferralPage.tsx` (reuse its
established table/toast/confirm-dialog conventions per `architecture.md`'s cited pattern:
"every mutation fires a matching `toast.success`/`toast.error` pair; every
[destructive] action [has] an inline confirm step").

- Header stat: "X of Y lifetime seats claimed" (live from `GetLifetimeInvitesQuery`'s
  redeemed count + the cap from §6), with the cap editable inline by an admin.
- "Generate invite" dialog: target role (Owner/Artist radio), optional email field,
  expires-in selector (a sensible preset list — e.g. 24h / 48h / 72h / 7 days — plus a
  custom date/time option; do not force only-hours or only-days, the request explicitly
  asked for "hours or days"), disabled with a clear inline message once the cap is
  reached rather than a generic error toast only.
- Table: code (with copy-link button building the same `/lifetime/{code}` URL the backend
  returns), target role, email (or "open link" when unset), status badge
  (Pending/Redeemed/Revoked/Expired — derived, not stored, per §4.3/§5.3), redeemed
  studio (linked, when present), expires-at, created-at. Row action: "Revoke" (confirm
  dialog) shown only when `Status == Pending`.
- Loading/error/empty states for the table itself — this is a first-class list page like
  every other admin list page in this codebase; do not ship it without the same
  loading-skeleton/error-retry/empty-state treatment `IssuerStudioListPage.tsx` (now
  presumably `AdminStudioListPage.tsx` post-rename — **confirm the actual current
  filename**) already established.

### 7.2 Public landing page — `/lifetime/:code`

No auth. Fetches `GetLifetimeInvitePreviewQuery` on mount. Four rendered states beyond
loading:

1. **Not found** — "This invite link isn't valid. Double-check the link, or contact the
   person who sent it to you."
2. **Expired** — "This offer has expired. [Register normally →]" (link to the ordinary
   signup flow — someone who missed the window should not hit a dead end).
3. **Already redeemed** — "This seat has already been claimed."
4. **Revoked** — same copy as "not found" is acceptable (do not distinguish revoked from
   not-found to a public visitor — no reason to expose that an admin actively pulled an
   invite; distinguishing them only matters to the admin, who sees it in their own table).
5. **Valid** — the actual offer page:
   - Real countdown computed from the server's `ExpiresAt` against the client clock (not
     a client-side timer that starts fresh on every page load/refresh from some
     "duration" value — it must be pinned to the real absolute deadline every single
     time, or it is not honest urgency, see §8.4).
   - Explicit literal date/time shown alongside the countdown, not only the ticking
     numbers (accessibility + honesty — see §8.5).
   - Copy naming this as a limited, one-time offer (Decision §2.1 makes this claim true).
   - If `IsEmailLocked`, a line noting "this offer is reserved for a specific email
     address" without stating what it is.
   - A single, prominent primary button reading exactly **"Reserve my seat"** (the
     request's own specified copy), routing into the existing registration flow
     (`StudioRegistrationPage` for `TargetRole.Owner`, the solo-artist signup page for
     `TargetRole.Artist`) with the code carried forward (query param or route state —
     match whatever this codebase's existing convention is for carrying a referral code
     from a public link into the registration form, e.g. `?ref=` on
     `RegisterStudioRequest`'s `ReferralCode`; mirror it for `?code=` /
     `LifetimeInviteCode`).

### 7.3 Registration form changes

Both registration forms silently attach `LifetimeInviteCode` from the query param/route
state on submit — no new visible field for the ordinary case. Server-side rejection
(§5.5) surfaces as a normal form-level error banner, same as any other
`BusinessRuleViolationException` today.

### 7.4 Success state

After a successful lifetime-code registration specifically (not every registration),
show a distinct success variant — reuse whatever success-page/success-state mechanism
the existing registration flow already has (confirm it exists; if registration today
just redirects straight into the app with no dedicated success screen, this is new UI,
not a modification of one) — with:

- A clearly rewarding, celebratory visual treatment (respect `prefers-reduced-motion` —
  no forced animation for a user who has asked the OS to reduce it, per §8.5).
- Explicit copy confirming lifetime status ("You've secured lifetime access to
  [Product Name] — free, forever, including everything we build next.").
- The promised follow-through: **"You'll receive a confirmation email at
  {the email they just registered with} shortly."** — this line's promise must be true;
  it ships in the same change as §5.6's actual email send, not before it and not as a
  copy-only placeholder.
- A path into the app (dashboard/setup checklist — whatever a normal successful
  registration already routes to).

### 7.5 Countdown accuracy against clock skew

The client should not trust its own `Date.now()` naively against a server-issued absolute
timestamp if this codebase has any existing pattern for server-time reconciliation (check
for one — e.g. a response header or a `/health`-adjacent endpoint already used elsewhere
for this). If none exists, a small, acceptable tolerance (recompute from the response's
own fetch time, accept ordinary client-clock drift) is fine — this is a marketing
countdown, not a security boundary; the **real** enforcement is server-side
`ExpiresAt < DateTime.UtcNow` at redemption (§5.5), which is authoritative regardless of
what the client's timer displays.

---

## 8. Cross-cutting requirements (restated per CLAUDE.md/architecture.md conventions —
this section is repetitive across every overnight prompt in this repo on purpose; do not
skip re-reading it because it looks familiar)

### 8.1 Tenant isolation (Rule #1)

`LifetimeInvite` is deliberately not tenant-scoped (§4.3) — every query against it uses
`IgnoreQueryFilters()` explicitly, admin-only or public-preview-only, never reachable
through an authenticated non-admin tenant context. No query anywhere in this feature may
omit `IgnoreQueryFilters()` and rely on an ambient `ICurrentTenant` that doesn't exist for
an anonymous caller — this is the exact bug class §3.4/the guest-checkout precedent
(`architecture.md`, approved usage #51) already documented as silently returning
zero-rows rather than throwing. **Write the anonymous-context integration test the same
way that precedent's fix was proven** — a real `AppDbContext` constructed with
`ICurrentTenant.StudioId == Guid.Empty`, not a unit test against a fake context that never
registers query filters at all.

### 8.2 RBAC at the endpoint (Rule #2)

`POST /api/v1/platform/lifetime-invites`, `GET /api/v1/platform/lifetime-invites`,
`POST /api/v1/platform/lifetime-invites/{id}/revoke` — all `AdminOnly`. The one new public
route (`GET /api/v1/lifetime-invites/{code}`) gets its required new `AllowAnonymous
Exceptions` table row (§5.7) in the same change — this is not optional, and CI's
`help-sync`-adjacent gates aside, there is a real architecture fitness-test posture in
this repo (`.github/workflows/ci.yml`'s "Architecture fitness tests" step,
`architecture.md`'s EPIC-0001 entry) that a new unprotected endpoint without a table row
is exactly the class of thing that convention exists to catch — do not introduce one
without the paperwork.

### 8.3 Never log PII (Rule #3)

`InvitedEmail` is never written to any log statement, structured or otherwise — the
existing `RegisterSoloArtistHandler`/`RegisterStudioHandler` catch-blocks already log only
`userId`/`studioId` on email-send failure; extend that exact posture, do not add
`req.OwnerEmail`/`invite.InvitedEmail` to any new `LogInformation`/`LogWarning` call this
feature adds. The audit log (§8.6) itself must also keep `Metadata` PII-scrubbed per
`AuditLogEntry`'s own doc comment — log `InvitedEmail`'s presence as a boolean
(`hadEmailLock: true/false`), never the address itself, mirroring exactly the
`IsEmailLocked`-not-`InvitedEmail` decision already made for the public preview response
(§5.4).

### 8.4 Fake-urgency / dark-pattern risk — this is a real compliance question, not a
nice-to-have

FTC guidance on deceptive design (dark patterns) explicitly calls out countdown timers
and "limited supply" claims as unlawful **when the underlying claim is false** — a timer
implying an offer expires when it doesn't, or a "only N left" claim when supply is not
actually constrained. This feature's countdown and scarcity framing are compliant
*because and only because* both claims are made true by this document's own
implementation: the expiry is a real, server-enforced deadline (§5.5's hard rejection
past `ExpiresAt`), and the "limited offer" framing is backed by a real, admin-set,
live-tracked global cap (§2.1/§6). **Do not let a future change quietly turn either claim
false** — e.g., do not add a "silently extend on request" admin override that lets an
expired invite still redeem without changing what the landing page told the visitor, and
do not let the seat cap become purely cosmetic (unenforced) while the copy keeps claiming
scarcity. If a future change needs to loosen either constraint, that is exactly the kind
of thing that must be flagged explicitly in that change's own Decisions Log entry, not
silently done.

### 8.5 Accessibility

- Countdown region: `aria-live="polite"`, updated at a sensible interval (once per
  minute is enough for a screen-reader user; do not fire a live-region update every
  second) — plus the literal expiry date/time rendered as static text alongside it
  (§7.2), so the offer's real deadline is never conveyed by the ticking number alone.
  This satisfies the spirit of WCAG 2.2.1 (Timing Adjustable)'s "essential, real-time
  event" exception (a genuine limited-time offer's own deadline is inherently not
  something the visitor can be allowed to adjust) without falling back on that exception
  as an excuse to under-communicate the deadline.
- Any celebratory success-page animation (§7.4) respects `prefers-reduced-motion` — show
  the static end-state (checkmark, confirmation text) immediately for a visitor who has
  that OS preference set, no forced motion.
- Standard form/contrast/keyboard-nav/touch-target conventions this codebase's existing
  `accessibility-audit-2026-09-05.md` already established apply to every new page here —
  do not treat a "growth/marketing" page as exempt from the same bar the rest of the
  product is held to.

### 8.6 Audit logging (sensitive-action standard)

New `AuditActions` constants: `LifetimeInviteCreated`, `LifetimeInviteRevoked`,
`LifetimeInviteRedeemed`. New `AuditTargetTypes.LifetimeInvite`. All three commands
(`CreateLifetimeInviteCommand`, `RevokeLifetimeInviteCommand`, and the redemption path
inside `RegisterStudioHandler`/`RegisterSoloArtistHandler` — **confirm whether
`IAuditableCommand` can attach to a command that is not itself the audited action's own
top-level command, i.e. whether `RegisterStudioCommand` gaining `IAuditableCommand`
conditionally, only when a lifetime code was redeemed, is a pattern this pipeline
supports, or whether the redemption needs its own internal follow-up command/notification
purely to get an audit row — read `AuditLogBehavior.cs` before deciding, do not guess**)
produce an audit row. `Metadata` includes `targetRole`, `hadEmailLock` (boolean, never
the email itself), `expiresAt` — never `InvitedEmail`.

### 8.7 Structured logs only (Rule #5)

No `Console.WriteLine`/`console.log` anywhere in this feature's backend or frontend
production paths — `ILogger<T>` throughout, matching every existing handler's pattern.

### 8.8 Secrets (Rule #4)

Nothing in this feature introduces a new secret/connection string/API key. The invite
`Code` is not a secret in the "rotate it if leaked" sense — it is single-use and
self-invalidating on redemption; treat it like `ReferralCode.Code`, not like a Stripe key.

---

## 9. Do not build blind — genuine open product/business questions

These get a fully-specified backlog note here, not a silent guess baked into the code.

### 9.1 The open-ended "every future feature, forever" commitment (flagged again,
deliberately, because it is the single biggest risk in this entire feature)

Decision §2.2 was made explicitly and is the literal build target — but it is worth
restating precisely what was accepted: **every future overnight prompt that ships a new
plan-gated feature now carries an extra, easy-to-forget obligation** (touch the seeded
Lifetime plan row too) that has no automated enforcement in this pass. Recommended,
NOT built in this pass (flagging, not deciding): a lightweight architecture-fitness test
(same family as the existing "no `PlatformLedger`" fitness test, §3.4/EPIC-0001) that
fails CI if a new nullable `Max*`-shaped field or new plan-gating boolean is added to
`Plan.cs` without a corresponding update to the seeded Lifetime row in the same commit.
This is real, buildable, and would close the gap §2.2 opened — but it's a second,
separable piece of work, not bundled into this pass without an explicit go-ahead.

### 9.2 Revenue/reporting treatment of Lifetime seats

Should a Lifetime `Subscription` count toward MRR/ARR on any admin analytics surface (the
existing MRR chart, `SubscriptionOversightPage`, etc.)? The honest answer is almost
certainly "$0 contribution, tracked separately as a 'Lifetime seats granted' count," but
this touches financial reporting the admin/founder may show to a future investor,
co-founder, or accountant — **do not silently decide this by however the existing MRR
query happens to behave once a `null`-Stripe-subscription Lifetime row exists in the same
table it scans.** Verify what the current MRR calculation actually does when it encounters
a `Subscription` with `StripeSubscriptionId == null` and `PlanId` pointing at a plan with
no `PlanPrice` rows — it may already silently correctly treat it as $0, or it may throw,
or it may misreport. This needs a verified answer, and if it needs a real fix (not just a
"confirmed fine"), that fix belongs in this pass since it directly touches code this
feature adds data to — but the *question of how Lifetime seats should be presented on any
future dedicated founder/investor-facing report* is Finance's call, not this pass's.

### 9.3 Tax/accounting treatment

Whether granting permanent $0 access has any tax/revenue-recognition implication in
Albania (or wherever the platform entity is domiciled) is explicitly Finance/Legal's
question, not engineering's — noted here only so it isn't lost, never answered here.

### 9.4 Support-cost / feature-scope-creep pattern this industry has already learned the
hard way

Current-market lifetime-deal practice (2026) is consistently **against** what was chosen
in §2.2 — the standard operator playbook explicitly recommends capping either the
lifetime window (e.g. contractually 3–5 years) or the feature surface (locked to
grant-date functionality, with genuinely new paid tiers excluded from LTD holders) rather
than an open-ended "everything, forever" promise, precisely because of the support-load
and long-run-liability pattern this document's own research surfaced. This was explicitly
raised before the decision in §2.2 was made and the product owner chose the open-ended
version anyway, with the mitigating process in §9.1 as the intended safety valve — this
is a **flagged, deliberate divergence from category best practice**, not an oversight,
and should be written up exactly that way in the Decisions Log (§13.3), the same way
`architecture.md` already documents the NIPT field and the above-benchmark social
verification investment as deliberate, sign-off'd divergences rather than silent
deviations.

---

## 10. Constraints (restated, every prompt)

- No new npm/NuGet package without flagging it first as a prerequisite decision. This
  feature should not need one — countdown timers, crypto-random strings, and a new
  MediatR command are all things this codebase's existing dependencies already cover.
- No `useEffect` for data fetching on the frontend (confirm and follow whatever this
  codebase's approved data-fetching pattern already is — RTK Query, per the tech stack
  list — for the public preview query and the admin list/generate/revoke mutations).
- TypeScript strict, no `any`, anywhere in new frontend code.
- Explicit C# types, no `var` for non-obvious types, anywhere in new backend code.
- No business logic in endpoints — MediatR handlers + FluentValidation validators only,
  same as every existing command in this codebase.
- Tenant isolation via EF Core global query filters everywhere except the explicitly
  approved, explicitly numbered `IgnoreQueryFilters()` usages this feature adds (§3.4,
  §5.3, §5.4, §5.5) — every one of them gets a table row and an inline code comment
  citing its number, not a bare unexplained `.IgnoreQueryFilters()` call.
- Every endpoint has `.RequireAuthorization()` with the correct policy, except the one
  documented, table-added `AllowAnonymous` exception (§5.7).
- Never log PII (§8.3).
- Structured logs only (§8.7).
- Tests ship with every change (§11) — not after, not "if time allows."

---

## 11. Test requirements

### Backend — unit

- `CreateLifetimeInviteHandlerTests`: happy path; cap-reached rejection; `ExpiresAt`
  out-of-allowed-range rejection; code-collision retry exhaustion.
- `RevokeLifetimeInviteHandlerTests`: happy path; rejects revoking an already-`Redeemed`
  invite; rejects revoking a non-existent id.
- `RegisterStudioHandlerTests` / `RegisterSoloArtistHandlerTests` — new sub-cases: valid
  Lifetime code → `Subscription.IsLifetimeGrant == true`, `Status == Active`,
  `PlanId == lifetimePlan.Id`, no trial-related jobs scheduled (for the owner path);
  invalid code (not found / wrong role / expired / already redeemed / revoked /
  email-mismatch) → registration fails entirely, no `Studio`/`Subscription` row
  persisted (verify via a real DB round-trip that nothing was left behind on the
  rejected path, not just that an exception was thrown).
- `CreateSubscriptionHandlerTests` — new sub-case: a `Subscription` with
  `IsLifetimeGrant == true` rejects any checkout attempt (§4.4's guard).
- `GetLifetimeInvitePreviewQueryHandlerTests` — every one of the five response states
  (not-found/expired/redeemed/revoked/valid), and confirms `InvitedEmail` never appears
  serialized in the response object under any state.

### Backend — integration (real MySQL + ASP.NET Identity, per this repo's established
posture)

- `LifetimeInviteRedemptionRaceIntegrationTests` (§5.5) — the single most important test
  in this feature; two concurrent redemption attempts against one single-use code, assert
  exactly one succeeds and the other receives a clear rejection, not a duplicate Studio.
- `LifetimeInvitePreviewAnonymousContextIntegrationTests` — constructed with
  `ICurrentTenant.StudioId == Guid.Empty`, proving `IgnoreQueryFilters()` is actually
  present and actually needed (per §8.1's explicit citation of the guest-checkout bug
  class — do not skip this because it "obviously" has `IgnoreQueryFilters()` in the code
  you just wrote; the whole point of that precedent is that it looked obviously-fine too).
- `LifetimeInviteEndpointAuthorizationTests` — real ASP.NET Core auth pipeline,
  confirming the three admin endpoints reject non-admin roles and the one public endpoint
  truly requires no auth (mirrors `MessagingEndpointAuthorizationTests`'s cited pattern).
- Full happy-path redemption for both `TargetRole.Owner` and `TargetRole.Artist`, against
  a real MySQL-backed context, confirming the resulting `Subscription` row's every field
  matches §5.5's spec exactly (not just "no exception thrown").

### Frontend — component

- Admin page: loading / error / empty / cap-reached-disables-generate / normal-populated
  states; generate dialog validation; revoke confirm-then-toast flow.
- Public landing page: all five preview states rendered distinctly; countdown renders and
  updates; `prefers-reduced-motion` respected on the success page's animation; the
  "Reserve my seat" button correctly carries the code into each of the two registration
  flows.
- Registration form: server-side rejection of an invalid/expired/mismatched code surfaces
  as a clear, distinct error banner (not a generic "something went wrong").

---

## 12. Help-sync obligations (mandatory, per feature, no exceptions)

1. **`frontend/src/features/help/helpContent.ts`** — two additions:
   - An admin-facing article under whatever existing admin/platform-administration
     category id already houses referral-code management (grep the file for the
     `PlatformReferralPage`/referral-code article's `id` and category and add this
     alongside it, under the *same* category — do not invent a new top-level category for
     one article) — covering: what a lifetime invite is, how to generate one, the seat
     cap, email-locking, revocation.
   - An owner-facing note under whatever existing Billing/Plans article covers plan
     tiers, mentioning that a studio may be on a Lifetime plan if the platform granted
     one, and that it never requires payment — a Lifetime-plan owner who opens Help
     looking for "why don't I see a billing/upgrade option" should find an answer, not a
     dead end.
2. **`frontend/public/user-manual/index.html`** (confirmed live copy, §3.4 — **re-verify
   this is still true before writing to it**; do not touch `docs/user-manual.html` and
   call this satisfied) — a new section under the admin/platform documentation area
   mirroring the two `helpContent.ts` additions above, plus a short note in whatever
   section documents plan tiers/billing for owners.
3. **Onboarding tours** — `adminTour.ts` gains a new step pointing at the "Generate
   Lifetime Invite" button/page (a genuinely new admin capability, tour-worthy).
   `ownerTour.ts`/`artistTour.ts` — **no new step**, and here is why, stated explicitly
   per the mandatory rule rather than silently skipped: the landing page, countdown, and
   "Reserve my seat" flow all happen *before* the redeemer has an account, entirely
   outside the in-app tour surface (tours run for an already-signed-in user exploring
   their own dashboard) — there is nothing inside the authenticated app for an owner/
   artist tour to point at that is unique to having arrived via a lifetime invite; their
   post-signup experience is the ordinary owner/artist dashboard, already covered by the
   existing tours.
4. CI's `help-sync` job (`.github/workflows/ci.yml`, §8.2) will independently check that
   this PR's gated-path changes came with a Help update — this should pass without
   needing `[skip-help-sync]`; if it doesn't, that is a signal something above was missed,
   not a reason to reach for the override.

---

## 13. Industry-standard benchmark note (CLAUDE.md Rule #6 — searched, not assumed)

**Vertical booking-SaaS benchmark set** (Vagaro, Fresha, Boulevard, Mindbody, Zenoti,
GlossGenius, Booksy, Mangomint, Schedulicity, Square Appointments): **none of these
vertical booking platforms run public lifetime-deal programs today** — this is
**explicitly flagged as a deliberate divergence from the benchmark set**, not presented as
benchmark-driven, per the same convention `architecture.md` already uses for the NIPT
field and the above-benchmark social-verification investment. "Lifetime deal" is a
bootstrapped/indie-SaaS growth-and-early-cashflow tactic (the AppSumo/associated-LTD-
marketplace pattern), not a vertical-booking-category norm — the closest legitimate
analog in this category is a founder doing manual, high-touch, sales-assisted onboarding
for a handful of anchor customers, which is exactly what this feature mechanizes (an
admin-issued, targeted, time-boxed link) rather than a public self-serve LTD marketplace
listing.

**Current (2026) operator practice for running this kind of program**, confirmed by
searching rather than assumed from training data: real lifetime-deal programs
consistently (a) cap total seats sold rather than running unlimited (Decision §2.1
follows this), and (b) cap either the time horizon or the feature surface rather than
promising literally everything forever (Decision §2.2 explicitly diverges from this,
flagged in §9.4, not silently).

**B2B SaaS platform-admin benchmark** (org/tenant management, audit logging, support
impersonation, API/webhook tiers — CLAUDE.md Rule #6's second benchmark set, for anything
touching the `admin` role): this codebase already meets the bar here independent of this
feature — impersonation sessions, structured audit logging, and API-key/webhook access
tiers are all already shipped (confirmed via migration history:
`AddImpersonationSession`, `AddStudioApiKeys`, `AddWebhooks`). This feature's own admin
surface (a generate/list/revoke invite-campaign tool with live seat tracking) is a
standard shape for that category — the closest analog is an enterprise SaaS's "generate a
VIP/beta onboarding code" internal tool — and slots naturally alongside the existing
`PlatformReferralPage`/`IssuerStudioListPage`(now admin-named)-family admin tooling rather
than requiring a new UX pattern.

---

## 14. Final verification checklist (run before declaring this done)

1. `dotnet build` clean, `dotnet test` green — report the before/after unit+integration
   test counts explicitly, the way every precedent prompt's Decisions Log entry does.
2. `pnpm tsc`/`pnpm lint`/`pnpm test`/`pnpm build` all clean — report before/after test
   counts.
3. A real migration applied to a real dev MySQL database (not just "the build compiles")
   — `Plan.IsPubliclyPurchasable`, `Subscription.IsLifetimeGrant`, and the new
   `LifetimeInvite` table all actually exist post-migration; the Lifetime `Plan` row is
   actually seeded and has zero `PlanPrice` rows.
4. `LifetimeInviteRedemptionRaceIntegrationTests` (§11) passes against the real
   concurrency scenario, not just against a mocked/serial call sequence.
5. Every new `IgnoreQueryFilters()` call has a matching numbered row in
   `architecture.md`'s "IgnoreQueryFilters Approved Usages" table, correctly numbered
   against whatever the *actual* current highest number is at build time (re-count, do
   not trust this document's "51" without re-checking).
6. The new `AllowAnonymous Exceptions` table row (§5.7) is present and accurate.
7. No PII (`InvitedEmail` or any registrant's email) appears in any log statement, any
   audit-log `Metadata` blob, or the public preview response — grep for it explicitly as
   a verification step, don't just eyeball the diff.
8. Help Menu (`helpContent.ts`, two articles), the **live** user manual
   (`frontend/public/user-manual/index.html`, verified live via `git log` before writing
   to it), and `adminTour.ts` (one new step) are all updated in the same change;
   `ownerTour.ts`/`artistTour.ts` are explicitly confirmed unchanged with the stated
   reasoning (§12.3), not silently left alone.
9. The countdown/expiry claims are verified true end-to-end: generate an invite, confirm
   the landing page's countdown matches the real `ExpiresAt`, confirm redemption is
   actually rejected once that moment passes (not just that the UI stops counting down).
10. The seat-cap claim is verified true end-to-end: with the cap set to a small test
    number, confirm invite *generation* is blocked at the cap while a previously-issued,
    still-`Pending` invite generated before the cap filled remains redeemable (per
    Decision §2.1's specific carve-out).
11. `CreateSubscriptionHandler`'s new guard (§4.4) is verified: attempting checkout
    against a Lifetime-plan studio is rejected, not silently allowed to create a real
    Stripe subscription alongside it.
12. No drift from the "do not touch" list below.
13. This document's own unresolved verification items are actually resolved, not carried
    forward unaddressed: the exact `RegisterStudioRequest`/multi-step-registration wiring
    (§3.2), the `AuditLogBehavior` timing question (§5.2/§8.6), the platform-wide-setting
    mechanism for the seat cap (§6), and the MRR-reporting behavior question (§9.2) each
    get a real, verified answer recorded in the Decisions Log entry (§13.3), not left as
    open questions in the shipped code's comments alone.

---

## 15. Do-not-touch list

- `ReferralCode`/`ReferralRedemption`/`AdminGenerateReferralCodeHandler` and everything
  under `PlatformReferralPage.tsx` — read for pattern-copying only (§3.4). This feature
  adds a sibling admin page and a sibling entity; it does not modify the referral system's
  existing behavior, schema, or tests in any way.
- `StudioJoinInvite` and its accept/decline flow — unrelated invite type, read for
  pattern-copying only, not touched.
- `PromoCode` and its silent-fail-at-booking behavior — cited for contrast in §5.5, not
  modified.
- Any Stripe webhook handler, `IStripeBillingService`/`IStripeDiscountService`
  implementation, or existing `CreateSubscriptionHandler` logic beyond the single new
  guard clause specified in §4.4 — this feature deliberately bypasses Stripe entirely for
  Lifetime studios; it does not touch how Stripe billing works for everyone else.
- `docs/user-manual.html` (stale copy, §3.4/§12.2) — do not edit it under the assumption
  it's the live one; if it turns out this document's "stale" determination was wrong,
  stop and flag that discrepancy rather than guessing which copy to update.
- Any existing `AllowAnonymous` endpoint's existing table row — only a new row is added
  (§5.7); no existing row's security mechanism is altered.
- Anything under `k8s/`, `docker/`, `.github/workflows/` beyond what CI already
  automatically enforces (no new pipeline steps needed for this feature).

---

## 16. Final deliverable spec

**Files created/updated (code):**
- `Pena_e_Arte.Domain/Entities/LifetimeInvite.cs` (new)
- `Pena_e_Arte.Domain/Entities/Plan.cs` (+`IsPubliclyPurchasable`)
- `Pena_e_Arte.Domain/Entities/Subscription.cs` (+`IsLifetimeGrant`)
- `Pena_e_Arte.Domain/Constants/AuditActions.cs` (+3 constants), `AuditTargetTypes`
  (+1 constant)
- `Pena_e_Arte.Domain/Utilities/SecureCodeGenerator.cs` (new, extracted per §4.3)
- `Pena_e_Arte.Application/Platform/Commands/CreateLifetimeInviteCommand.cs`,
  `RevokeLifetimeInviteCommand.cs` (new)
- `Pena_e_Arte.Application/Platform/Queries/GetLifetimeInvitesQuery.cs`,
  `GetLifetimeInvitePreviewQuery.cs` (new)
- `Pena_e_Arte.Application/Auth/Commands/RegisterSoloArtistCommand.cs` (redemption logic)
- `Pena_e_Arte.Application/Studios/Commands/RegisterStudioCommand.cs` (redemption logic)
- `Pena_e_Arte.Application/Billing/Commands/CreateSubscriptionCommand.cs`
  (`IsLifetimeGrant` guard)
- `Pena_e_Arte.Contracts/Requests/RegisterStudioRequest.cs`,
  `RegisterSoloArtistRequest.cs` (+`LifetimeInviteCode`)
- `Pena_e_Arte.Contracts/Responses/LifetimeInviteResponse.cs`,
  `LifetimeInvitePreviewResponse.cs` (new)
- `Pena_e_Arte.Infrastructure/Persistence/Configurations/LifetimeInviteConfiguration.cs`
  (new), `AppDbContext.cs` (+`DbSet<LifetimeInvite>`, admin-level no-filter section),
  `IAppDbContext.cs` (same)
- `Pena_e_Arte.Infrastructure/Services/MailKit/EmailRenderer.cs`
  (+`RenderLifetimeInviteConfirmation`)
- DataSeeder file (located per §4.5) — seed the Lifetime `Plan` row
- A new EF Core migration (name it descriptively, e.g. `AddLifetimeInvites`)
- `Pena_e_Arte.API/Endpoints/PlatformEndpoints.cs` (or wherever admin platform routes
  live) — 3 admin routes; a public route for the preview query (confirm which endpoints
  file already hosts other public `/api/v1/public/...`-style or code-only lookups like
  the gift-card-balance route, and add this alongside it)
- Frontend: new admin page + route, new public landing page + route, registration form
  changes (both flows), success-state addition, `helpContent.ts`, `adminTour.ts`,
  `frontend/public/user-manual/index.html`
- Full test suites per §11

**Docs (this pass, written by the *implementing* session as part of its own definition of
done — not by this consultation project):**
- `docs/claude/architecture.md` — new Feature Module Map row, new Decisions Log entry
  (§13.3 content: the deliberate divergences from the referral-code redemption-timing
  pattern §5.5, from PromoCode's silent-fail posture §5.5, and from lifetime-deal
  category best practice §9.4; the new standing "keep the Lifetime plan generous" rule
  from §2.2/§9.1; the resolved answers to every open verification item in §14.13), new
  `IgnoreQueryFilters()` Approved Usages table rows (correctly numbered), new
  `AllowAnonymous Exceptions` table row (§5.7).
- `CLAUDE.md` or `docs/claude/architecture.md` (whichever already houses process-level
  standing rules) — the new rule from §9.1: every future plan-gated feature must extend
  the seeded Lifetime plan row in the same change.

**Exact commit message to use** (implementing session, after its own work is verified
per §14):

```
feat: add admin-issued lifetime access invite links

Admin can generate a time-boxed, single-use invite that grants the
redeemer's studio a permanent, unbilled Lifetime plan (unlimited limits,
all perks on) instead of the normal trial/Free path — for owner
registrations and solo-artist signups alike. Public countdown landing
page at /lifetime/:code enforces a real server-side expiry and an
admin-configurable global seat cap; redemption is fully validated and
committed inside the existing registration handlers, not as a separate
checkout step.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
```

(Replace/augment the trailing attribution line with whatever this repo's own
`CONTRIBUTING.md`-documented commit convention requires, if it specifies one beyond what's
shown here — confirm before committing.)
