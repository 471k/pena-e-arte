# Overnight Prompt — Independent / Solo Artist Signup (No Pre-Registered Studio)

> Date: 2026-08-26
> Target: `Pena_e_Arte.Domain` (2 new `Studio` fields + 1 new entity), `Pena_e_Arte.Contracts`
> (new requests/responses, `StudioResponse` extended), `Pena_e_Arte.Application` (new
> `Auth/Commands/RegisterSoloArtistCommand.cs`, new `Studios/StudioJoinInvites/` folder,
> targeted edits to `UpdateMyStudioCommand.cs`, `CreateSubscriptionCommand.cs`,
> `GetStudioMapQuery.cs`, `GetPublicStudioQuery.cs`, `GetNearbyStudiosQuery.cs`),
> `Pena_e_Arte.Infrastructure` (2 EF migrations — see Phase 1 and Phase 6 — plus `IIdentityService`
> role/claim-swap usage in Phase 6, DataSeeder untouched), `Pena_e_Arte.API` (2–3 new endpoints),
> `frontend/src/features/auth`, `frontend/src/features/studios`, `frontend/src/features/artists`
> (new `StudioJoinInvites` UI), backend + frontend tests, Help Menu (`helpContent.ts`),
> standalone user manual (`index.html`), onboarding tours.
> Two new EF Core migrations, both additive/nullable-safe — no destructive schema change, no
> backfill risk beyond a straightforward default-value backfill on existing rows (Phase 1).
> No new NuGet or npm packages — reuses existing Identity, MediatR, FluentValidation, Hangfire,
> and RTK Query wiring throughout.
> Work unsupervised. Commit after every Phase. All changes must pass `dotnet build`,
> `dotnet test`, `pnpm tsc --noEmit`, `pnpm lint`, and `pnpm test --run` before the session ends.
> **Phases 1–5 are the core ask and must ship.** Phase 6 (studio-join/dissolution) is the one
> genuinely novel piece of this prompt with no existing precedent to mirror — if by the time you
> reach it you cannot get its Identity role/claim-swap fully covered by passing tests you are
> confident in, stop, commit Phases 1–5 as a complete and correct increment, and write up exactly
> what's left for a follow-up prompt instead of shipping a half-verified tenant-claim change.
> Tenant isolation (CLAUDE.md Rule #1) is non-negotiable — when in doubt in Phase 6, don't guess.

---

## Pre-flight

1. Read `CLAUDE.md`, `docs/claude/backend.md`, `docs/claude/database.md`,
   `docs/claude/frontend.md`, `docs/claude/conventions.md`, and this file in full before making
   any changes.
2. Baseline, before touching anything:
   - `dotnet build`
   - `dotnet test` — note the current pass count; pre-existing failures are not this prompt's
     problem, but do not introduce new ones.
   - `pnpm tsc --noEmit`
   - `pnpm lint`
   - `pnpm test --run` — confirm the current suite is green first.
3. Read each of these **in full** before starting the matching Phase — they are the exact
   precedents this prompt's new code must mirror, not just similar-in-spirit examples. Every
   design decision below was verified against these files as they exist today (2026-08-26); if
   any of them has changed since, trust the live source over this document and flag the
   discrepancy in your commit message.
   - `Pena_e_Arte.Application/Studios/Commands/RegisterStudioCommand.cs` +
     `Pena_e_Arte.Application/Studios/Validators/RegisterStudioValidator.cs` — Phase 2's studio
     auto-provisioning reuses this handler's slug-uniqueness loop and trial/subscription
     construction shape almost verbatim, minus the NIPT/City/Lat-Long requirements.
   - `Pena_e_Arte.Application/Auth/Commands/RegisterUserCommand.cs` +
     `Pena_e_Arte.Application/Auth/Validators/RegisterUserValidator.cs` — Phase 2's Identity-user
     creation and email-verification-send logic mirrors this handler's owner-registration branch.
   - `Pena_e_Arte.Application/Artists/Commands/CreateOwnArtistProfileCommand.cs` — **unchanged by
     this prompt**, but it is the exact mechanism a solo artist uses to attach their own `Artist`
     row to their auto-provisioned studio. Read it so Phase 7 (frontend) wires into it correctly
     instead of duplicating it.
   - `Pena_e_Arte.Application/Artists/Commands/CreateArtistCommand.cs` — read in full, especially
     the "already belongs to an existing account" branch and its comment block. **Phase 6 does
     not modify this file.** That branch's cross-tenant rejection is a deliberate, tested 2026-08-21
     security fix (see `docs/claude/architecture.md` Decisions Log, "Owner-as-artist cross-tenant
     invite fix"); do not weaken or bypass it. Phase 6 builds an entirely separate,
     dual-consent path instead.
   - `Pena_e_Arte.Application/Billing/Commands/CreateSubscriptionCommand.cs` — the Free-plan
     no-Stripe branch (`price.Price == 0 ? DateTime.UtcNow.AddYears(50) : ...`) is exactly what
     Phase 2's studio auto-provisioning must replicate for the initial `Subscription` row.
   - `Pena_e_Arte.Application/Auth/Commands/SwitchStudioCommand.cs` and `LeaveStudioCommand.cs` —
     the closest existing precedent for a user's active-tenant claim changing after the fact.
     Phase 6 reuses `IIdentityService.EnsureTenantClaimAsync`/`RemoveTenantClaimAsync`/
     `IssueTokensForTenantAsync` the same way, even though (unlike the client multi-studio case)
     it is swapping roles as well as tenants — read `IIdentityService.cs` and its
     implementation (`IdentityService.cs`, find it under `Pena_e_Arte.Infrastructure`) in full
     before writing Phase 6, and confirm exactly how roles are added/removed there (there is no
     existing `RemoveRoleAsync`/`AddRoleAsync` call in any current flow — you will likely need to
     add one to `IIdentityService`, following its existing method-doc-comment style).
   - `Pena_e_Arte.Application/Studios/Commands/UpdateMyStudioCommand.cs` +
     `UpdateMyStudioValidator` — confirms `Nipt` is **already** fully optional at this layer
     (`.When(x => !string.IsNullOrWhiteSpace(...))`) — Phase 3's auto-publish logic is a small,
     additive change to this exact handler, not a new command.
   - `Pena_e_Arte.Application/Common/Behaviors/PlanLimitBehavior.cs` and
     `IPlanLimitService`/`PlanLimitService.cs` — read the exact quota-check signature before
     writing Phase 6's accept-invite quota check. It must check the **inviting studio's**
     `MaxArtists` usage, not the caller's currently-active tenant (at accept time the caller's
     JWT still carries their old solo-studio tenant claim) — confirm whether the existing
     `IQuotaCheckedCommand` pipeline behavior can take an explicit `StudioId` or only ever reads
     `ICurrentTenant`; if only the latter, do not force-fit the existing pipeline behavior —
     call `IPlanLimitService` directly inside the handler instead, scoped explicitly to
     `invite.StudioId`.
   - `frontend/src/features/studios/components/RegisterStudioPage.tsx` — the current, only
     owner-registration entry point (`/register`). Read its full form/mutation flow
     (`useRegisterStudioMutation` → `useRegisterUserMutation` → auto-login) before deciding how
     Phase 7's solo-artist entry point plugs in alongside it.
   - `frontend/src/features/artists/components/ArtistListPage.tsx` — the existing UI for an
     owner attaching their own `Artist` profile via `CreateOwnArtistProfileCommand`. Phase 7's
     onboarding step reuses this same form, resurfaced as a guided step rather than something the
     user has to go find.

---

## Context — current state (verified against live source, 2026-08-26)

- **`Artist : TenantEntity` has a required, non-nullable `StudioId`.** Every artist row belongs
  to exactly one `Studio` by construction — this is the tenant key the global query filters, the
  JWT `tenant_id` claim, and `ICurrentTenant` all key off. This prompt does **not** make
  `Artist.StudioId` nullable and does not weaken any query filter — see "Do Not" section below.
- **There are today exactly two ways to become an `Artist`, and both require an
  already-registered `Studio`:** `CreateArtistCommand` (an existing owner invites someone into
  their already-registered studio) and `CreateOwnArtistProfileCommand` (an existing owner attaches
  their own profile to their own already-registered studio). There is no path for a person with
  no studio and no invite to become an artist at all.
- **`RegisterStudioCommand`/`RegisterStudioValidator` require a valid Albanian NIPT** (business
  tax ID, regex `^[A-Z]\d{8}[A-Z]$`), a `Name`, `City`, and bounded `Latitude`/`Longitude` before
  a `Studio` row — and therefore any artist under it — can exist at all. This is real friction
  for someone who wants to try the platform as a solo/independent tattooer before formalizing a
  business registration, or who works from home/mobile without a fixed studio address.
- **There is already a "studio-less" precedent, just not for artists.** `RegisterUserCommand`
  explicitly supports `role: "client"` with `StudioId == null` — a client registers with no
  studio at all, and their first `Client` row is created on demand later, by
  `SwitchStudioHandler`, the first time they book somewhere. This prompt's solo-artist path is
  the same spirit applied to the owner+artist side, not an entirely new pattern for this
  codebase.
- **A `Free` plan already exists, seeded, and is exactly the right fit.** `DataSeeder.
  ReconcileCoreTiersAsync` seeds `FreePlanId` ("Free", price 0, `MaxArtists = 1`,
  `MaxAppointmentsPerMonth = 15`, `MaxNotificationsPerMonth = 50`, `MaxStorageGb = 1`,
  `MaxLocations = 1`). `CreateSubscriptionHandler`'s no-Stripe branch already has the correct
  "never expires" handling for it: `periodEnd = price.Price == 0 ? DateTime.UtcNow.AddYears(50) :
  DateTime.UtcNow.AddMonths(1)`. **No changes to the Free plan itself, its limits, or this
  sentinel logic are in scope** — Phase 2 reuses it as-is for the initial `Subscription` a solo
  studio gets.
- **`PlanLimitBehavior`/`IQuotaCheckedCommand` already generically enforces `Plan.MaxArtists`.**
  `CreateArtistCommand` already carries `QuotaType.Artists`. This means once a solo studio is on
  the `Free` plan (`MaxArtists = 1`), an attempt to invite a second artist is **already** blocked
  automatically, with no new code — it forces the natural upgrade-to-paid-plan path. Verify this
  with a test in Phase 5; do not re-implement it.
- **`Nipt` is already nullable end-to-end at the entity and update layers.** `Studio.Nipt` is
  `string?` on the entity itself (comment: "nullable for backfill, not an auth factor"), and
  `UpdateMyStudioCommand`/`UpdateMyStudioValidator` already accept and validate an optional
  `Nipt` (only validated *if* provided). Only `RegisterStudioValidator` (the normal studio
  signup path) makes it mandatory. This means "add a NIPT later" for a graduating solo studio
  needs **zero new backend code** — the existing Studio Settings save flow already supports it.
- **`architecture.md`'s own "IsActive vs IsPublished" section explicitly names the exact trigger
  condition this feature creates**, and explicitly pre-authorizes the fix: *"If a future feature
  requires a studio to be active but unlisted (e.g. soft-launch mode), add `IsPublished bool` to
  `Studio` at that time and update this section."* `Studio.IsActive` currently gates **both**
  public visibility (Studio Map, `/discover` Studios tab, `/s/{slug}`) **and** whether tenant
  access is permitted at all — so it cannot be reused to hide an unpublished solo studio from
  directories without also locking its owner out of their own account. Phase 1 adds the
  `IsPublished` field this doc already anticipates; Phase 4 wires it into exactly the surfaces
  the doc names, and only those.
- **`GetPublicArtistQuery` (backing `/artist/{slug}`) filters on `Studio.IsActive` only, not on
  any studio-directory concept**, and is a separate public surface from the studio-directory
  ones above. This prompt deliberately leaves it untouched — a solo artist's own portfolio page
  must be public and bookable from the moment they finish `CreateOwnArtistProfileCommand`, even
  before their studio is "published." That immediacy is the actual point of this feature: get
  bookable fast, formalize the studio-level presentation later, on their own time.
- **`CreateArtistHandler`'s 2026-08-21 cross-tenant guard already rejects inviting an existing
  owner account from a different studio**, by design (tenant-isolation fix, see Decisions Log).
  This means an existing studio owner today gets a hard failure, with no recovery path, if they
  try to invite an independent solo artist by email. Phase 6 exists to give that a real,
  consent-gated resolution without touching the guard itself.
- **No token-based "pending invite, accept later" concept exists anywhere today.**
  `CreateArtistCommand` creates the Identity user and `Artist` row immediately and fires an
  invite *notification* email (`scheduler.EnqueueArtistInvite`) — there's no separate acceptance
  step for a brand-new artist. Phase 6 introduces the first such "pending, must be explicitly
  accepted by the invitee" concept in this codebase, scoped narrowly to the solo-artist-join
  case only. Do not generalize it to the normal `CreateArtistCommand` flow — that flow's
  immediate-creation behavior is intentional and unrelated to this prompt.

---

## Decisions locked in (already made — do not re-litigate)

1. **NIPT is fully optional for solo studios, permanently — including once they accept card or
   cash payments.** Do not add any gate, warning-that-blocks-action, or payment-time NIPT
   requirement for an `IsSolo` studio anywhere in this prompt. (Normal, non-solo studio
   registration keeps its existing mandatory NIPT — unchanged.)
2. **Scope includes both directions of studio conversion:** a solo artist formalizing their own
   solo studio into a fully public one (Phase 3/Phase 5), and a solo artist joining an existing,
   different studio (Phase 6).
3. This prompt goes straight to implementation — there is no separate spec-review document to
   reconcile against. Treat the "Context" section above as the verified factual basis and the
   Phases below as the actual specification.

## Open product question — flagged, not silently resolved

**What happens to a solo studio's historical data (appointments, clients, portfolio, payments)
when its owner dissolves it to join another studio (Phase 6)?** The recommended default this
prompt specifies — retain everything, mark the studio permanently inactive, do not copy or merge
any of it into the new studio — is implemented as the actual behavior below. This mirrors the
one existing cross-tenant precedent in this codebase (`PortableProfileService`, opt-in and
client-profile-only) rather than inventing a new cross-tenant data-copy mechanism, and avoids an
unattended session making an irreversible-deletion call unilaterally. If you judge, while
implementing Phase 6, that this default is wrong, **do not silently change it** — implement it as
specified, and write up the disagreement plainly in your commit message / final summary for a
human to decide.

---

## Phase 1 — Schema: `Studio.IsSolo` / `Studio.IsPublished`

1. Add two fields to `Pena_e_Arte.Domain/Entities/Studio.cs`:
   ```csharp
   /// <summary>True for a studio auto-provisioned by RegisterSoloArtistCommand for an
   /// independent artist with no pre-existing studio. Never set any other way.</summary>
   public bool IsSolo { get; set; }

   /// <summary>Controls listing in studio-directory surfaces only (Studio Map, /discover
   /// Studios tab, StudioPortfolioPage) — distinct from IsActive, which gates tenant access.
   /// True by default for every normally-registered studio. False on creation only for an
   /// IsSolo studio, until it has real City/Latitude/Longitude (see UpdateMyStudioHandler).
   /// See architecture.md's "IsActive vs IsPublished" section — this is the field that
   /// section names as the correct fix for exactly this situation.</summary>
   public bool IsPublished { get; set; } = true;
   ```
2. `RegisterStudioHandler` (normal path): explicitly set `IsSolo = false, IsPublished = true` on
   the new `Studio` (matches the field defaults, but set explicitly for clarity/searchability).
3. Add both columns to `StudioConfiguration.cs` with sensible defaults (`is_solo` NOT NULL
   DEFAULT FALSE, `is_published` NOT NULL DEFAULT TRUE) — snake_case per `database.md`'s naming
   convention.
4. `dotnet ef migrations add AddStudioSoloAndPublishedFlags --project Pena_e_Arte.Infrastructure
   --startup-project Pena_e_Arte.API`. Confirm the generated migration backfills existing rows
   to `is_solo = false, is_published = true` (should be automatic via the column defaults — verify
   the generated SQL explicitly rather than assuming).
5. Extend `StudioResponse` (`Pena_e_Arte.Contracts/Responses/StudioResponse.cs`) with
   `IsSolo` and `IsPublished`. **Grep for every `new StudioResponse(` call site** — at minimum
   `RegisterStudioHandler`, `GetStudiosHandler`, `UpdateMyStudioHandler`, `GetMyStudioQuery`'s
   handler, and any others found by the grep — and update every one; a missed call site is a
   compile error, so the build will catch it, but do not rely on the compiler alone — read each
   site to make sure the values passed are correct (`studio.IsSolo, studio.IsPublished`, not a
   hardcoded literal copy-pasted from another site).
6. Update `docs/claude/database.md`'s "Studio Entity Fields" table and `docs/claude/
   architecture.md`'s Feature Module Map (extend entry #11, "Public Portfolio Pages," or add a
   new row — your call, but the Studio Map / DiscoverPage / StudioPortfolioPage sections all need
   a one-line note that they now also filter on `IsPublished`) and the "IsActive vs IsPublished"
   section itself (replace its "no such field exists" framing with what was actually built).
   Add a Decisions Log entry following the existing entries' two-column (What / Why) format.
7. Commit.

---

## Phase 2 — Backend: solo-artist self-registration

1. New contract `Pena_e_Arte.Contracts/Requests/RegisterSoloArtistRequest.cs`:
   ```csharp
   public record RegisterSoloArtistRequest(
       string Email,
       string Password,
       string FirstName,
       string LastName);
   ```
   Deliberately no `City`/`Nipt`/`Latitude`/`Longitude`/`Slug` — all deferred, per Context.
2. New `Pena_e_Arte.Application/Auth/Commands/RegisterSoloArtistCommand.cs`:
   - `public record RegisterSoloArtistCommand(RegisterSoloArtistRequest Request) : IRequest;`
     (no response body needed — mirrors `RegisterUserCommand`'s shape; the frontend logs in
     separately afterward, exactly like normal owner registration does today).
   - Validator: `Email` (`NotEmpty().EmailAddress()`), `Password` (`NotEmpty().MinimumLength(8)`
     — same bar as `RegisterUserValidator`; do not invent a stricter policy for just this one
     endpoint), `FirstName`/`LastName` (`NotEmpty().MaximumLength(100)` — matches
     `CreateOwnArtistProfileValidator`'s bounds).
   - Handler, injecting `IAppDbContext db, IIdentityService identity, IEmailRenderer
     emailRenderer, INotificationService notifications, IAppSettings appSettings,
     ILogger<RegisterSoloArtistHandler> logger` (same set `RegisterUserHandler` uses, minus
     `IJobScheduler` — no trial-expiry jobs, see below):
     1. Slug generation for the `Studio`: `SlugHelper.GenerateSlug($"{req.FirstName}
        {req.LastName}")`, then the same while-loop uniqueness-suffix pattern
        `RegisterStudioHandler` uses against `db.Studios.AnyAsync(s => s.Slug == slug, ct)`.
     2. Build the `Studio`:
        ```csharp
        Studio studio = new()
        {
            Name = $"{req.FirstName} {req.LastName}",   // editable later via UpdateMyStudioCommand
            Slug = slug,
            City = string.Empty,
            OwnerEmail = req.Email,
            Nipt = null,
            Latitude = 0,
            Longitude = 0,
            IsActive = true,
            IsSolo = true,
            IsPublished = false,
            TrialExpiresAt = DateTime.UtcNow,   // not trialing — see Subscription below; kept
                                                  // non-null only because the column is non-nullable
                                                  // and unused once Subscription.Status is Active
        };
        ```
        Do not call `RegisterStudioHandler`/`RegisterStudioCommand` — this is a separate,
        parallel construction path with different required fields, not a wrapper around it.
     3. Build the `Subscription` directly on the `Free` plan, active immediately, no trial:
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
            CurrentPeriodEnd = DateTime.UtcNow.AddYears(50),   // mirrors CreateSubscriptionHandler's
                                                                 // Free-plan sentinel exactly
            GracePeriodEnd = DateTime.UtcNow.AddYears(50),
        };
        ```
        **Do not** call `jobs.ScheduleTrialExpiryWarning`/`ScheduleTrialExpiry`/
        `ScheduleGracePeriodEnd` — there is no trial to expire. (Verify `Plan.Name == "Free"` is
        actually how `ReconcileCoreTiersAsync` names it — re-read that method; if the working
        name changed per the free-tier-plan feature request's own note about marketing
        confirming final naming, look it up by the seeded `FreePlanId` constant instead of by
        name string, whichever `DataSeeder` actually exposes as a reusable constant today.)
     4. `db.Studios.Add(studio); db.Subscriptions.Add(subscription); await
        db.SaveChangesAsync(ct);`
     5. Create the Identity user with the **`owner`** role (not `artist` — see Context: this
        mirrors the existing, already-working "owner who is also an artist" pattern via
        `CreateOwnArtistProfileCommand`, rather than inventing a second role model):
        `identity.CreateUserAsync(req.Email, req.Password, "owner", studio.Id, req.FirstName)`.
        On failure, throw `BusinessRuleViolationException` with the joined errors — same as
        `RegisterUserHandler`. **Do not** add the owner-email-must-match-an-existing-studio check
        `RegisterUserHandler` has for its `role == "owner"` branch — that check exists precisely
        because normal owner registration expects the studio to already exist; here, this handler
        *is* what creates it, in the same request, so there is nothing to cross-check against.
     6. Send the identical email-verification flow `RegisterUserHandler` sends (same
        `GenerateEmailConfirmationTokenAsync` → `RenderEmailVerification` → `SendEmailAsync`
        sequence, same non-fatal try/catch with a `LogWarning` on failure — copy this block
        verbatim rather than re-deriving it, so any future change to verification copy/behavior
        only has to be made in one place... actually, if you can factor this into a small shared
        helper both handlers call without changing either's existing behavior, do that instead of
        duplicating; if it's not a clean extraction without touching `RegisterUserHandler`'s own
        tested behavior, duplicating verbatim is acceptable — your call, state which way you went).
   - `dotnet ef migrations` — none needed for this Phase; it's pure Application/API code against
     Phase 1's schema.
3. New endpoint in the appropriate `Pena_e_Arte.API` auth endpoint group:
   `POST /api/v1/auth/register/solo-artist`, `AllowAnonymous` (this is a new, explicitly-approved
   addition to the `AllowAnonymous` exceptions list in `architecture.md` — add it there, same
   format as the existing entries), calling `RegisterSoloArtistCommand` via MediatR, `202
   Accepted` or `204 NoContent` matching `RegisterUserCommand`'s existing endpoint's response
   shape exactly (read that endpoint's current status code before choosing — consistency over a
   fresh choice).
4. Unit tests: happy path (Studio/Subscription/Identity user all created correctly, `IsSolo =
   true, IsPublished = false, Nipt = null`, `Subscription.Status = Active` on the `Free` plan,
   `CurrentPeriodEnd` ~50 years out, no trial jobs scheduled — assert the job scheduler mock was
   never called at all), duplicate email (Identity's own uniqueness surfaces the same class of
   error `RegisterUserHandler`'s duplicate-email path does today — verify what that actually
   looks like and match it), slug collision (two solo artists named the same thing get suffixed
   slugs, same as the existing studio/artist slug uniqueness tests already cover elsewhere).
5. Commit.

---

## Phase 3 — Backend: auto-publish on real studio details

1. In `UpdateMyStudioHandler.Handle`, **after** the existing `City`/`Latitude`/`Longitude` writes
   and **before** `SaveChangesAsync`, add:
   ```csharp
   if (studio.IsSolo && !studio.IsPublished &&
       !string.IsNullOrWhiteSpace(studio.City) &&
       (studio.Latitude != 0 || studio.Longitude != 0))
   {
       studio.IsPublished = true;
   }
   ```
   Log this transition at `Information` level (`tenant_id`/`studio_id` only, per CLAUDE.md Rule
   #3 — no studio name/owner email in the structured log fields). This is a one-way transition in
   this handler — do not add a way to un-publish here; that's out of scope (an issuer can already
   fully deactivate a studio via `SuspendStudioCommand` if ever needed).
2. `(0, 0)` as "not yet set" is a deliberate, cheap sentinel — real-world coordinates landing
   exactly on Null Island are not a realistic false-positive for this product's userbase. Do not
   add a more elaborate "has location been set" tracking field for this.
3. Unit test: an `IsSolo` studio with default `City=""`/`Lat=0`/`Lng=0` calling
   `UpdateMyStudioCommand` with a real city and coordinates flips `IsPublished` to `true`; a
   non-solo studio's `IsPublished` is untouched by this handler either way (it's already `true`
   and stays `true`); an `IsSolo` studio updating only its `PhoneNumber` (city/coords unchanged,
   still blank) does **not** flip `IsPublished`.
4. Commit.

---

## Phase 4 — Backend: gate studio-directory surfaces on `IsPublished`

Change these three query handlers to filter on `IsActive && IsPublished` where they currently
filter on `IsActive` alone. Read each handler's current full source before editing — do not
guess field/method names.

1. `GetStudioMapQuery`/`GetStudioMapHandler` (`Pena_e_Arte.Application/Studios/Queries/
   GetStudioMapQuery.cs`) — change `.Where(s => s.IsActive)` to `.Where(s => s.IsActive &&
   s.IsPublished)`.
2. `GetPublicStudioQuery` (backs `StudioPortfolioPage`, `/s/{slug}`) — locate it (likely under
   `Pena_e_Arte.Application/Public/Queries/` or similar — find it by its actual location rather
   than assuming the path), add the same `IsPublished` condition wherever it currently checks
   `IsActive`.
3. The nearby-studios query backing `/discover`'s Studios tab, `GET /api/v1/public/studios/
   nearby` (`GetNearbyStudiosQuery` per `architecture.md`'s DiscoverPage section) — same change.
4. **Do not** touch `GetPublicArtistQuery` (backs `ArtistPortfolioPage`, `/artist/{slug}`) — it
   stays `IsActive`-only, per Context. A solo artist must be publicly bookable immediately, even
   while `IsPublished = false`.
5. **Do not** touch `GetStudiosQuery` (the unfiltered, all-studios list) unless you confirm by
   reading its callers that it is issuer-only/admin tooling, not a public surface — if it truly is
   issuer-only, leave it returning every studio regardless of `IsPublished` (an issuer needs to
   see unpublished solo studios too), but do add `IsSolo`/`IsPublished` to its output columns if
   the issuer studio-list UI would benefit from showing them (frontend judgment call, not
   mandatory).
6. Tests: for each of the three changed queries, add a case with an `IsSolo, !IsPublished, IsActive`
   studio and assert it's excluded; assert a normal `IsActive, IsPublished` studio is still
   included (regression guard).
7. Commit.

---

## Phase 5 — Backend: verify (not rebuild) the natural growth path

This Phase is mostly verification plus one small addition — the enforcement already exists.

1. Add (or confirm existing coverage of, if you find it already covered) an integration test:
   a solo studio on the `Free` plan (`MaxArtists = 1`, already has its one `Artist` via
   `CreateOwnArtistProfileCommand`) attempting `CreateArtistCommand` for a second artist throws
   the existing quota-exceeded exception from `PlanLimitBehavior`. Do not change
   `PlanLimitBehavior`, `IPlanLimitService`, or the `Free` plan's `MaxArtists` value — this is a
   verification test, not new enforcement logic.
2. In `CreateSubscriptionHandler.Handle`, after `subscription.PlanId = command.Request.PlanId;`,
   add: if the studio this subscription belongs to has `IsSolo == true` and the newly-chosen
   `plan.Id != freePlanId` (i.e., they're upgrading off Free to a real paid tier), set
   `subscription.Studio.IsSolo = false`. This is a small, additive, purely-cosmetic/analytics
   flag flip — it does not gate anything by itself (a solo studio already has full functional
   access; this just stops describing a now-multi-artist-capable, paying studio as "solo").
   Verify `subscription.Studio` is actually loaded/tracked at that point in the method (it's
   `.Include(s => s.Studio)`'d earlier in the handler — confirm before assuming it's safe to
   mutate here).
3. Unit test for the above: a solo studio's `Subscription` upgraded to `Growth` flips
   `Studio.IsSolo` to `false`; upgrading to `Free` itself (a no-op case, but exercise it) does not.
4. Commit.

---

## Phase 6 — Backend: solo artist joins an existing (different) studio

Read the Pre-flight note on this Phase again before starting. This is the one part of this
prompt with no existing precedent to mirror closely — build it carefully, test it thoroughly,
and stop rather than guess if `IIdentityService`'s actual role/claim primitives don't support
what's specified below cleanly.

### 6a. New entity: `StudioJoinInvite`

`Pena_e_Arte.Domain/Entities/StudioJoinInvite.cs` — **not** a `TenantEntity` subtype the normal
way (it must be readable/writable by the invited artist's account, which is *not* a member of the
inviting studio's tenant until accepted) — model it like `Studio`/`Plan`/`Subscription`
(issuer-level shape, own `StudioId` column, no query filter applied), consistent with how those
cross-tenant-adjacent entities are already handled in `AppDbContext`:
```csharp
public class StudioJoinInvite
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid StudioId { get; set; }              // the inviting studio
    public string InvitedEmail { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? Specializations { get; set; }
    public decimal? HourlyRate { get; set; }
    public StudioJoinInviteStatus Status { get; set; } = StudioJoinInviteStatus.Pending;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime ExpiresAt { get; set; }          // CreatedAt + 14 days, mirrors trial-length precedent
    public DateTime? RespondedAt { get; set; }

    public Studio Studio { get; set; } = null!;
}
public enum StudioJoinInviteStatus { Pending, Accepted, Declined, Expired }
```
Add `DbSet<StudioJoinInvite>` to `AppDbContext` under the "Issuer-level (no tenant filter)"
section (it genuinely isn't tenant-scoped the normal way — both parties involved are in different
tenants by definition), with a clear code comment explaining why, mirroring the existing
`IgnoreQueryFilters()` approved-usages table's documentation style. Add it to that table
(`docs/claude/database.md`) as a new approved case, not as a silent exception.
`StudioJoinInviteConfiguration.cs` per the entity-configuration pattern; add a unique index on
`(StudioId, InvitedEmail)` where `Status == Pending` if MySQL/EF Core makes a filtered unique
index easy here — otherwise enforce "no duplicate pending invite" in the handler instead of the
schema, and say which you did.

New migration: `dotnet ef migrations add AddStudioJoinInvite ...`.

### 6b. Owner side — `InviteSoloArtistToJoinCommand`

New command, `OwnerOnly`, separate from and **not modifying** `CreateArtistCommand`:
- Request: same shape as `CreateArtistRequest` (`Email, FirstName, LastName, Specializations,
  HourlyRate`).
- Handler: verify the invited email actually belongs to an `owner`-role account whose *only*
  studio has `IsSolo == true` (reuse `identity.GetUserRolesAsync`/`GetTenantIdsAsync` +
  `db.Studios.IgnoreQueryFilters().Where(s => s.OwnerEmail == req.Email && s.IsSolo)` the same way
  `CreateArtistHandler`'s existing branch already resolves the account) — if not, throw a clear
  `BusinessRuleViolationException` telling the inviting owner this isn't a solo-artist account
  (direct them to the normal `CreateArtistCommand` flow instead, or explain the email is taken by
  something else entirely). If a `Pending` invite to this email from this studio already exists,
  throw rather than create a duplicate. Otherwise create the `StudioJoinInvite`
  (`ExpiresAt = CreatedAt.AddDays(14)`), send a notification email to the invited artist ("X
  studio wants you to join as an artist — sign in to accept or decline"), and return a response
  DTO with the invite's id/status.
- Endpoint: `POST /api/v1/studios/me/join-invites`, `OwnerOnly`.
- This is a **new, additive** entry point. Do not change what `CreateArtistCommand` does when it
  hits the same "email belongs to another owner" case — that still throws exactly as it does
  today. (Frontend Phase 7 is what turns that existing error into "invite them to join instead"
  as a follow-up action, not this handler.)

### 6c. Artist side — list + accept/decline

- `GetMyStudioJoinInvitesQuery` — `AuthenticatedOnly` (any authenticated role, but only rows
  matching `currentUser.Email` and `Status == Pending` and `ExpiresAt > now` are ever returned) —
  returns pending invites addressed to the caller's own email, with the inviting studio's public
  `Name`/`Slug`/`City` so the frontend can render "Join {Name} in {City}?" without exposing
  anything not already public.
- `AcceptStudioJoinInviteCommand(Guid InviteId)`:
  1. Load the invite; 404 if missing/not `Pending`/expired/not addressed to `currentUser.Email`
     (case-insensitive) — same "404 not 403" convention this codebase already uses elsewhere for
     ownership checks (cited in the manual-reminders precedent read in Pre-flight).
  2. Confirm the caller is currently the `owner` of exactly one `IsSolo == true` studio (their
     own solo studio) — if not (e.g. they've already dissolved it, or this isn't actually a solo
     artist account), throw `BusinessRuleViolationException`.
  3. Quota check: read `IPlanLimitService` directly (per the Pre-flight note — do not rely on the
     `IQuotaCheckedCommand` pipeline behavior here since it resolves the caller's *current*
     tenant, which is still the old solo studio at this point) against `invite.StudioId`'s
     `MaxArtists`. If full, throw a clear, actionable error — do not silently proceed.
  4. Create the new studio's `Artist` row using `invite.FirstName/LastName/Specializations/
     HourlyRate`, the caller's existing `UserId`, `StudioId = invite.StudioId` — reuse
     `CreateArtistHandler`'s slug-generation/uniqueness logic (factor it into a small shared
     static helper both handlers call, rather than copy-pasting the while-loop a third time,
     since Phase 2 also needs a variant of it — your call on exact factoring, but don't leave
     three independent copies of the same uniqueness loop in the codebase after this prompt).
  5. Soft-close the old solo studio: `studio.IsActive = false`. Add a new `Studio.ClosedAt
     DateTime?` field (Phase 6's second, small additive migration alongside `StudioJoinInvite` —
     or fold both into one migration, your call) and set it to `DateTime.UtcNow`. **Do not**
     delete the `Studio` row or any of its data (appointments, clients, portfolio images,
     payments) — see "Open product question" above; this is the specified default.
  6. Identity/claims: remove the `owner` role and the old studio's `tenant_id` claim from the
     user; add the `artist` role and a `tenant_id` claim for the new studio; issue fresh tokens
     scoped to the new studio (reuse `EnsureTenantClaimAsync`/`IssueTokensForTenantAsync`, and add
     whatever role-add/role-remove primitive `IIdentityService` is missing, per the Pre-flight
     note). **Write an explicit integration test that, after acceptance, a token request against
     the old studio's id fails and the user cannot use owner-only endpoints against it anymore** —
     this is the single most important correctness property in this whole prompt; do not consider
     Phase 6 done without that test passing for real, not just compiling.
  7. Mark the invite `Accepted`, `RespondedAt = now`.
  8. Return fresh `AuthResponse`-shaped tokens (mirrors `SwitchStudioResponse`'s shape) so the
     frontend can seamlessly re-authenticate the user into their new artist identity without a
     forced logout/login round-trip.
- `DeclineStudioJoinInviteCommand(Guid InviteId)` — simpler: 404 by the same rules, set `Status =
  Declined, RespondedAt = now`. No other side effects.
- Endpoints: `GET /api/v1/auth/join-invites`, `POST /api/v1/auth/join-invites/{id}/accept`,
  `POST /api/v1/auth/join-invites/{id}/decline` — all `AuthenticatedOnly` (any role; the handlers
  themselves enforce the actual eligibility checks above, matching this codebase's existing
  "404, not 403" ownership-check convention rather than a narrower RBAC policy that would leak
  existence information).
- Tests: full happy path (invite → accept → artist row exists at new studio → old studio
  inactive+closed → old-studio tokens rejected → new-studio tokens work → `AcceptStudioJoinInviteCommand`
  is idempotent-safe against a double-click, i.e. a second accept attempt 404s since `Status` is
  no longer `Pending`); quota-full rejection; expired-invite rejection; decline path; an invite
  addressed to an email that is not actually a solo-owner account (defensive — should not be
  reachable via 6b's own validation, but test the accept handler's own check independently in
  case of a race where the account changed between invite creation and acceptance).

### 6d. Commit.

---

## Phase 7 — Frontend

1. **Solo-artist entry point.** Add a segmented choice at the top of
   `RegisterStudioPage.tsx` — "I run a studio" (existing full form, unchanged) vs. "I'm an
   independent artist" (new, minimal form: First name, Last name, Email, Password,
   Confirm password only — reuse `PasswordStrengthMeter`/`PasswordInput` as the existing form
   does). On submit, call a new `useRegisterSoloArtistMutation` (add to `authApi.ts`, following
   the existing RTK Query endpoint conventions in that file) against Phase 2's endpoint, then the
   same "check your email to verify" confirmation state the existing owner-registration success
   path already shows (reuse it, don't reinvent). State plainly in your commit message whether
   you implemented this as a toggle within the existing page/component/tests, or as a new sibling
   route+component — either is acceptable, but pick one and update
   `RegisterStudioPage.test.tsx` (or add a new test file) accordingly; don't leave the choice
   half-done.
2. **Post-verification onboarding.** After a solo owner logs in for the first time with no
   `Artist` row of their own yet (`myStudio.isSolo === true` and `GetMyArtistQuery` returns
   nothing), route them straight into the existing `CreateOwnArtistProfileCommand` form (the one
   already built inside `ArtistListPage.tsx`) as a guided first step, rather than requiring them
   to find the Artists page on their own. Read how `ArtistListPage.tsx` currently triggers that
   form and reuse the same component/mutation — do not duplicate the form.
3. **"Finish setting up your studio" prompt.** While `myStudio.isSolo && !myStudio.isPublished`,
   show a persistent, dismissible-per-session (not permanently dismissible — it should reappear
   next login until actually resolved) banner in the owner layout linking to the existing Studio
   Settings page, explaining that adding a real city/location makes them discoverable on the
   Studio Map and in `/discover`. No new "Publish" button is needed — saving real details on the
   existing settings form is what flips `IsPublished` (Phase 3, backend-only).
4. **Studio-join invites UI**, for the invited artist: a small notification/list surface
   (new — place it wherever this codebase's existing pattern for "things needing the user's
   attention" lives; check `NotificationLog`/the bell-icon notification center pattern first
   before inventing a new UI location) showing pending `StudioJoinInvite`s via
   `GetMyStudioJoinInvitesQuery`, each with Accept/Decline actions. **The Accept action must show
   an explicit confirmation dialog before calling the accept mutation**, stating in plain language
   that their current solo studio will be closed (not deleted — its data is kept, but it becomes
   permanently inaccessible to them as an owner) and that they'll become an artist at the new
   studio instead. Do not let this be a single-click action.
5. **Inviting-owner UI**, for the `CreateArtistCommand`-fails-with-"already belongs to an existing
   account" case: when that specific error is returned, offer "Send a request to join instead"
   (calling Phase 6b's new endpoint) rather than a dead-end error message, on the existing
   Artists-page invite form.
6. Update `frontend/src/app/router.tsx` for any new routes.
7. `pnpm tsc --noEmit`, `pnpm lint`, `pnpm test --run` after each sub-step, not just at the end.
8. Commit.

---

## Phase 8 — Help sync (CLAUDE.md Rule #7 — mandatory, same change)

- `frontend/src/features/help/helpContent.ts`: new article(s) covering (a) signing up as an
  independent artist, what "solo" means, what the Free plan includes; (b) publishing your studio
  once you have a real location; (c) growing beyond one artist (upgrade path); (d) receiving and
  accepting a studio-join invite, and what happens to your solo studio when you do. Cover both
  Owner-role and Artist-role framing where the same underlying feature reads differently
  depending which side of it the reader is on.
- `frontend/public/user-manual/index.html`: corresponding sections, matching the existing
  manual's structure/tone.
- `frontend/src/features/help/tours/*.ts`: add a branch or a short dedicated variant for the
  solo-artist first-run experience if the existing owner/artist tours' steps reference things
  (like "invite your team" or studio-address setup) that don't make sense in the same order for a
  solo signup. State explicitly in your commit message which way you went, per this codebase's
  existing convention for this exact judgment call (see the manual-reminders precedent's own
  "touched only if... state which way you went" note).
- Commit.

---

## Verification checklist (must all be green before ending the session)

- `dotnet build`
- `dotnet test` — no regressions vs. the Pre-flight baseline count; all new tests from Phases
  2–6 passing.
- `pnpm tsc --noEmit`
- `pnpm lint`
- `pnpm test --run`
- Both new migrations apply cleanly (`dotnet ef database update`) **and the app actually boots**
  against the migrated database — this codebase has a standing, named lesson
  (`docs/claude/feedback_di_wiring_verification.md`) about new MediatR-scanned handlers needing
  DI-wiring verification beyond a green build; this prompt adds several new handlers
  (`RegisterSoloArtistCommand`, `InviteSoloArtistToJoinCommand`, `GetMyStudioJoinInvitesQuery`,
  `AcceptStudioJoinInviteCommand`, `DeclineStudioJoinInviteCommand`) — do not skip this step.
- Manual smoke path (describe what you did in the commit message, even if scripted rather than
  literally clicked through): register as a solo artist → verify email → log in → create own
  artist profile → confirm publicly bookable at `/artist/{slug}` while `/s/{slug}` and the Studio
  Map still exclude it → update studio settings with a real city/location → confirm it now
  appears on the Studio Map → attempt to invite a second artist on the Free plan and confirm the
  existing quota block fires → (Phase 6) have a different studio invite the solo artist to join,
  accept it, and confirm the old studio is inactive/closed while the new artist row and studio
  access work correctly, including that old-studio tokens are truly rejected afterward.

---

## Hard Rules Reminder

- **Tenant isolation (Rule #1):** no change to `Artist`'s tenant-key shape or any existing query
  filter. `StudioJoinInvite` is deliberately unfiltered/issuer-level-shaped and documented as
  such, following the existing precedent for entities that aren't naturally single-tenant.
  Phase 6's role/claim swap is the one place this prompt asks you to do something genuinely new
  with tenant claims — treat it with the same rigor as the 2026-08-21 cross-tenant fix this
  prompt repeatedly cites, and prove with a real test that the old studio is truly inaccessible
  afterward.
- **RBAC (Rule #2):** every new endpoint has `.RequireAuthorization()` with a real policy or an
  explicit, deliberate `AllowAnonymous` (only Phase 2's registration endpoint, added to the
  approved-exceptions list in `architecture.md`) — no accidentally-unprotected endpoint.
- **Never log PII (Rule #3):** solo artist emails/names never appear in structured log
  properties anywhere in this prompt's new code — `user_id`/`studio_id`/`invite_id` only.
- **Secrets never in source (Rule #4):** no new secrets/config introduced.
- **Structured logs only (Rule #5):** Serilog only, no `Console.WriteLine`/`console.log`.
- **Industry standard (Rule #6):** this entire feature closes a real gap against this product's
  benchmark set — Fresha, Vagaro, Boulevard, GlossGenius, Mindbody, Zenoti all support a
  single-provider/solo business signing up and taking bookings without a formal multi-staff
  business registration step blocking them first. This prompt's design (auto-provisioned "solo
  studio," deferred business details, natural upgrade path via existing plan-quota enforcement)
  matches that category standard while changing the least possible amount of this codebase's
  existing tenant model to get there.
- **Help sync (Rule #7):** Phase 8, same change, not a follow-up.

---

## Do Not

- Do not make `Artist.StudioId` nullable, or add any "studio-less artist" concept at the entity/
  query-filter level. Every artist, solo or not, belongs to exactly one `Studio` row, always —
  the "solo" part is that the `Studio` is auto-provisioned and initially unpublished, not that
  tenancy itself becomes optional.
- Do not modify `CreateArtistHandler`'s existing cross-tenant rejection logic (the 2026-08-21
  fix). Phase 6 is additive alongside it, never a change to it.
- Do not weaken or make optional the NIPT requirement on the **normal** (non-solo)
  `RegisterStudioCommand`/`RegisterStudioValidator` path. This prompt's NIPT-optional decision
  applies to `IsSolo` studios only.
- Do not add an `IsPublished`-style bypass anywhere in issuer-facing, cross-tenant tooling —
  issuer views remain unfiltered (`IgnoreQueryFilters`) exactly as today; `IsPublished` is a
  public-surface concept only.
- Do not invent a generalized "pending invite, accept later" flow for the normal
  `CreateArtistCommand` path. `StudioJoinInvite` is scoped narrowly to the solo-artist-join case.
- Do not delete or cross-tenant-copy a dissolved solo studio's historical data in Phase 6 — retain
  and deactivate only, per the "Open product question" section.
