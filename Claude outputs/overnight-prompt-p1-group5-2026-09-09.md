# Overnight Master Prompt — P1 Backlog, Group 5 ("Final Decision-Gated + Completion")

**Date:** 2026-09-09
**Mode:** Autonomous overnight build. Work through the phases in order. Do not stop to ask
clarifying questions — every open design question below has been pre-resolved, either by
explicit product decision (noted per phase) or by live-codebase verification during this
session. If you hit a genuine blocker not covered here, stop that phase, document it in the
Decisions Log per the Final Deliverable section, and move to the next phase rather than
guessing silently.
**Run with:** a fresh branch off `main`, e.g. `feat/p1-group5-2026-09-09`.
**Before starting:** read `CLAUDE.md` in full and skim `docs/claude/architecture.md`'s Decisions
Log for "Plan usage limits" and the two-sided referral precedent
(`docs/claude/overnight-prompt-two-sided-referral-rewards-2026-07-18.md`) — read the correction
below before assuming that document's mechanism transfers directly to this one.

## Context

This is the sixth and final master prompt executing the 2026-09-09 P1 backlog audit
(`docs/claude/audit-p1-backlog-2026-09-09.md` against
`docs/claude/p1-backlog-master-build-spec-2026-09-09.md`). Already shipped: Group 1 (CSV export,
booking style field, PWA), Group 2 (studio hours, timezone), Group 4 (promo codes, dunning,
custom intake fields, flash catalog), Group 3 (waitlist, booth-rent, gift cards, packages). This
prompt covers the three remaining items that had open questions or were flagged as
"completion, not new-build": Client-to-Client Referral (#4), Marketing Email Campaigns (#10), and
Plan Usage-Limit Enforcement completion (#19).

The studio owner has resolved the open questions for #4 and #10:

| Item | Question | Decision |
|---|---|---|
| Client Referral (#4) | One-sided vs. two-sided reward | **Two-sided** — both referrer and referee rewarded |
| Client Referral (#4) | Reward form | **Percent off next deposit** |
| Marketing Campaigns (#10) | Default marketing consent for existing clients | **Opted out by default — studio must ask** |

Item #19 needed no product decision — it's a completion task against already-built plan-limit
infrastructure — but live-codebase research found the original spec's proposed approach for the
storage dimension technically unworkable as written; see Phase 3 below.

**One backlog item remains permanently outside this document's scope: Support Impersonation
(#15).** It needs a live product/security conversation about its admin-endpoint allow-list, not
a spec. Nothing in this prompt touches it.

**Build order:** Phase 1 (Client Referral) → Phase 2 (Marketing Campaigns) → Phase 3 (Plan
Usage-Limit Completion). Phase 1 touches `CreateAppointmentCommand.cs`, which by now (if Groups 3
and 4 are already merged) also carries the `PromoCode` and `GiftCard` discount logic — Phase 1
adds a third, and must respect the stacking order documented below rather than reordering what's
already there.

## Required reading before touching code

- `docs/claude/overnight-prompt-two-sided-referral-rewards-2026-07-18.md` — the **platform-level**
  two-sided referral mechanism (studio-to-studio signup incentive, rewards the referrer via a
  Stripe coupon applied to their subscription). Read the correction below before assuming this
  mechanism transfers to client-to-client referrals — it does not, for a specific, verifiable
  reason.
- `Pena_e_Arte.Application/Appointments/Commands/CreateAppointmentCommand.cs` — if Group 3/4 are
  merged, this file already has `PromoCode` and `GiftCard` discount logic after the
  `DepositCalculator.Calculate(...)` line; Phase 1 adds a third discount source at the same site.
- `Pena_e_Arte.Domain/Entities/Client.cs` — already has a `MarketingOptIn bool` field (default
  false) with a doc comment explaining it's the account-level marketing consent captured at
  guest checkout / manual Add Client. Read this before Phase 2 — the original spec's proposed new
  `ClientProfile.MarketingEmailOptOut` field is redundant with this existing one and must not be
  added.
- `Pena_e_Arte.Domain/Interfaces/IR2Service.cs` and
  `Pena_e_Arte.Infrastructure/Jobs/GuestPendingUploadCleanupJob.cs` — `IR2Service.ListByPrefixAsync`
  already exists and returns per-object size; `GuestPendingUploadCleanupJob` is the closest working
  example of a job that lists R2 objects by prefix. Phase 3's storage-quota job is built the same
  way.
- `Pena_e_Arte.Infrastructure/Services/PlanLimitService.cs` — confirms `Studio.StorageUsageBytes`
  is read for quota checks but **never written anywhere** in the current codebase, and
  `QuotaType.Locations` always reports `1` with an explicit comment that this dimension "never
  blocks until [multi-location] ships."

## Constraints (apply to every phase)

- No new npm or NuGet packages.
- No `useEffect` for data fetching — RTK Query hooks only.
- TypeScript strict, no `any`.
- All business logic through MediatR commands/queries; no logic in endpoint lambdas beyond
  mapping.
- Every new endpoint: explicit `.RequireAuthorization("<Policy>")` or a documented
  `AllowAnonymous` exception (none of this group's phases need a new anonymous endpoint — if you
  find yourself adding one, stop and reconsider, since nothing here was specified as
  guest-facing).
- Every new entity needs an EF Core migration; every `TenantEntity` subclass gets the standard
  global query filter.
- Tests: unit tests for every new validator/handler; integration tests for every new endpoint,
  covering the happy path and the RBAC/tenant-isolation boundary.
- Help sync (CLAUDE.md rule #7): every user-facing addition updates `helpContent.ts`, the
  standalone manual, and the relevant onboarding tour file in the same change.

---

## PHASE 1 — Client-to-Client Referral Program (#4)

### Design decisions (pre-resolved)

**Product decisions (confirmed):** two-sided reward (both referrer and referee get a discount);
reward form is a flat percent off the next deposit for both parties (no owner-configurable
fixed-vs-percent choice — simpler than `PromoCode`, which does offer both, because this is a
platform-standard mechanic, not something each studio configures per code).

**Correction to the original spec — do not port the two-sided referral precedent's mechanism.**
The existing two-sided reward system
(`docs/claude/overnight-prompt-two-sided-referral-rewards-2026-07-18.md`) rewards a referring
*studio* by applying a **Stripe coupon directly to that studio's platform subscription** — this
works because platform SaaS billing is still Stripe-direct (`HandleSubscriptionUpdatedCommand`,
unaffected by the Amendment A restriction). Client-to-client referral rewards are a completely
different money surface: a *client's* deposit payment on their *own* appointment — Flow A
(client → studio), the surface `IStripePaymentService` was deleted from and `IPaymentProvider`/
`NullPaymentProvider` now fail-closed-by-design pending POK. There is no Stripe subscription to
attach a coupon to for a client, and Flow A doesn't use Stripe coupons even when it does have a
live provider. **Do not build a coupon-based mechanism here.** Instead, reward the referee's
discount inline at booking time (same in-app pattern `PromoCode` and gift-card redemption already
use), and reward the referrer with a redeemable credit applied to *their own future* booking,
since the referrer isn't necessarily booking anything at the moment their code gets redeemed.

**Discount-stacking order** — by this point in the backlog, a single booking can carry up to
three separate discount sources: a `PromoCode` (Group 4), a redeemed `GiftCard` balance (Group 3),
and now a `ClientReferralReward` credit (this phase). Apply them in this fixed order, each to
whatever remains after the previous one, floored at 0: **promo code → gift card → referral
reward credit.** This extends (does not contradict) the "promo code first, then gift card" order
already documented in Group 3's Gift Cards phase — referral reward is simply appended as the
third step at the same insertion point in `CreateAppointmentCommand.cs`.

Guard against self-referral (`ReferrerClientId == bookingClientId`) and limit each client to
redeeming any given referral code at most once — enforced by a unique index, not just
application-level checking (the same "DB-enforced, not just application-checked" posture
`Payment.AppointmentId`'s unique index already models in this codebase).

### Backend

- `Pena_e_Arte.Domain/Entities/ClientReferralCode.cs`:
  ```csharp
  public class ClientReferralCode : TenantEntity
  {
      public Guid ReferrerClientId { get; set; }
      public string Code { get; set; } = string.Empty;   // unique per studio
      public decimal RewardPercent { get; set; }          // flat percent, both sides — no per-code choice
      public int RedemptionCount { get; set; }
  }
  ```
  No `RewardType` enum (confirmed: still doesn't exist anywhere in this codebase as of this
  session, and isn't needed — the product decision fixed the reward form to percent-only for
  both parties, simpler than the `AmountFixed`/`AmountPercent` duality `PromoCode` needed).
  Migration `AddClientReferralCodes`, unique index on `(StudioId, Code)`.
- `Pena_e_Arte.Domain/Entities/ClientReferralRedemption.cs`:
  ```csharp
  public class ClientReferralRedemption : TenantEntity
  {
      public Guid ClientReferralCodeId { get; set; }
      public Guid RedeemedByClientId { get; set; }
      public Guid AppointmentId { get; set; }    // the referee's booking the reward applied to
  }
  ```
  Unique index on `(ClientReferralCodeId, RedeemedByClientId)` — one redemption per client per
  code (and since each client only ever has one active `ClientReferralCode` as its own, this in
  practice caps a given referrer→referee pair at one redemption ever).
- `Pena_e_Arte.Domain/Entities/ClientReferralReward.cs` (new — the referrer's own credit,
  structurally similar to a single-use `PromoCode` scoped to one client):
  ```csharp
  public class ClientReferralReward : TenantEntity
  {
      public Guid ClientId { get; set; }                 // the referrer who earned this
      public Guid SourceRedemptionId { get; set; }        // which redemption earned it
      public decimal RewardPercent { get; set; }
      public bool IsRedeemed { get; set; }
      public Guid? RedeemedOnAppointmentId { get; set; }
  }
  ```
  Migration `AddClientReferralRewards` (can combine with the migration above).
- `GetOrCreateMyReferralCodeCommand` — auto-generates a code for a client on first request
  (idempotent — check for an existing row before creating), `ClientAndAbove`.
- Redemption hook in `CreateAppointmentCoreAsync` (`CreateAppointmentCommand.cs`), at the same
  site as the promo-code/gift-card logic, as the third step: accept an optional `ReferralCode`
  string on the booking request. If present: look up an active `ClientReferralCode` by code
  (studio-scoped), reject self-referral, reject if `RedeemedByClientId` already has a redemption
  row for this code (unique-index-backed, but check first for a clean error message rather than
  relying on the DB exception), apply `RewardPercent` to whatever deposit remains after promo
  code + gift card, record the `ClientReferralRedemption`, increment `RedemptionCount`, **and**
  create a new `ClientReferralReward` row for `ReferrerClientId` with the same `RewardPercent` —
  this is the two-sided part: the referrer doesn't get their discount applied to anything right
  now, they get a credit they can spend on their own next booking.
- Extend the same booking-request handling with an optional `ReferralRewardId` (the referrer
  redeeming their own earned credit on their own booking) — when present, verify
  `ClientReferralReward.ClientId` matches the booking client and `!IsRedeemed`, apply
  `RewardPercent` to whatever remains after the other three discount sources, mark
  `IsRedeemed = true` and set `RedeemedOnAppointmentId`.
- `GetMyReferralRewardsQuery` — `ClientAndAbove`, lists a client's own unredeemed
  `ClientReferralReward` rows (so the frontend can show "you have a 10% credit available" on
  their next booking).

### Frontend

- Client: "Refer a friend" card (new component) showing their code + a share link, redemption
  count, and any unredeemed reward credits.
- Referral-code entry field added to `BookAppointmentForm.tsx` alongside the existing
  `ReferralSource` field — do not conflate the two (`ReferralSource` is "how did you hear about
  us" marketing attribution; this is a reward-bearing code). Visually distinct from Group 4's
  promo-code field and Group 3's gift-card field — a booking form can now have up to three
  separate discount-code entry points; keep them clearly labeled ("Promo code", "Gift card",
  "Referral code") rather than one ambiguous "discount code" field.
- When a client has an unredeemed `ClientReferralReward`, surface it as a pre-checked "Apply your
  referral credit" option on their own booking form (distinct from the referral-code entry field
  above, which is for redeeming someone *else's* code).

### Tests

- Unit: self-referral rejected; one redemption per client per code enforced; stacking order
  (promo → gift card → referral) computed correctly across all combinations, floored at 0;
  referrer's `ClientReferralReward` created on successful referee redemption, with matching
  `RewardPercent`.
- Integration: `GetOrCreateMyReferralCodeCommand` idempotency; full referral flow end-to-end
  (referrer gets code → referee books with it → referee's deposit reduced → referrer has a new
  unredeemed reward → referrer books with `ReferralRewardId` → referrer's deposit reduced,
  reward marked redeemed).

### Help sync

- `client-referrals` help entry + manual section covering both sides of the two-sided mechanic
  (earning a code, redeeming someone else's, redeeming your own earned credit).

---

## PHASE 2 — Marketing Email Campaigns (#10, email-only)

### Design decisions (pre-resolved)

**Product decision (confirmed):** existing clients are opted OUT of marketing email by default;
the studio must explicitly get opt-in. **This decision is already the codebase's existing
default behavior** — see the correction below.

**SMS is explicitly out of scope for this phase**, per the original spec's own recommendation
("email-only is the lower-risk starting point... do not build SMS campaigns in the same pass").
Nothing in this phase adds SMS sending.

**Correction to the original spec — do not add `ClientProfile.MarketingEmailOptOut`.**
`Client.cs` (not `ClientProfile.cs`) already has a `MarketingOptIn bool` field, defaulting
`false`, with an existing doc comment: `"Sign up for news and updates" — account-level marketing
consent, captured at guest checkout or in the manual Add Client form. Default false (opt-in,
never opt-out-by-default)`. This is exactly the field the studio owner's decision calls for —
opted out until the client (or studio, on their behalf) opts them in — and it already exists,
already defaults correctly, and is already captured at two points in the client lifecycle (guest
checkout, manual Add Client). Adding a second, differently-named, inverted-polarity field on a
different entity would create two sources of truth for the same consent. **Use `Client.MarketingOptIn`
directly as the audience filter for `CampaignAudience.AllClients`** (and as a hard filter applied
to every audience option, including `Custom` — no campaign send should ever reach a client with
`MarketingOptIn == false`, regardless of which audience segment selected them for other reasons).
The unsubscribe link required on every campaign email should call a `WithdrawMarketingOptInCommand`
(new — thin, sets `MarketingOptIn = false`, `AllowAnonymous` since a clicked email link carries no
session, secured by a signed token in the link rather than auth — mirror the signed-token pattern
`IInstagramStateSigner`/`ISocialOAuthStateSigner` already use elsewhere in this codebase for
exactly this "anonymous but must prove identity" shape) rather than the studio manually managing
a separate opt-out list.

`Plan.AllowMarketingCampaigns` must be a real, enforced flag from day one — this repo's own
architecture.md documents an entire prior overnight round dedicated to un-hiding
`AllowApiAccess`/`PrioritySupport` for being sold-but-undelivered flags on `Plan`. Do not repeat
that mistake here: if `Campaign`/`SendCampaignCommand` ship gated behind
`Plan.AllowMarketingCampaigns`, the gate must actually block when false, verified by an
integration test, not just a UI-hidden toggle.

### Backend

- `Pena_e_Arte.Domain/Entities/Campaign.cs`:
  ```csharp
  public class Campaign : TenantEntity
  {
      public string Subject { get; set; } = string.Empty;
      public string BodyHtml { get; set; } = string.Empty;
      public CampaignAudience Audience { get; set; }   // AllClients, ClientsWithNoRecentVisit, Custom
      public CampaignStatus Status { get; set; }        // Draft, Sending, Sent, Failed
      public DateTime? SentAt { get; set; }
      public int RecipientCount { get; set; }
      public int DeliveredCount { get; set; }
  }
  ```
  Migration `AddCampaigns`. Add `Plan.AllowMarketingCampaigns bool` (default false) in the same
  migration — check `PlanLimitBehavior`/wherever `AllowApiAccess`/`PrioritySupport` are actually
  enforced (find that enforcement site, since these are plain booleans, not
  `IQuotaCheckedCommand` quota dimensions — likely a direct check in the relevant handler) and
  mirror that exact enforcement mechanism for the new flag, not a new one.
- `CreateCampaignCommand`/`SendCampaignCommand` — `OwnerOnly`, `SendCampaignCommand` checks
  `Plan.AllowMarketingCampaigns` and throws `BusinessRuleViolationException` if false (or
  whatever exception the `AllowApiAccess`-style enforcement already uses — match it).
  `IAuditableCommand` on `SendCampaignCommand`.
- Audience resolution: `AllClients` → every `Client` with `MarketingOptIn == true`;
  `ClientsWithNoRecentVisit` → same filter, further restricted to clients with no completed
  appointment in the last N days (pick a sensible default, e.g. 90, and expose it as a parameter
  on the command rather than hardcoding it silently); `Custom` → an explicit client-id list
  supplied by the owner, still hard-filtered by `MarketingOptIn == true` (a custom list is a
  starting set to narrow, never a way to bypass consent).
- Fan-out: a new Hangfire-enqueued (not recurring-scheduled — this fires once per
  `SendCampaignCommand` call, not on a cron) job, `SendCampaignJob`, iterating the resolved
  audience and calling `INotificationService.SendEmailAsync` per recipient with a rate-limited
  batch pace (check whether `INotificationService`'s email path already has provider-side rate
  limiting via Resend, or whether this job needs its own throttle — don't assume, verify against
  `EmailRenderer.cs`/wherever Resend calls are made). Each email includes the unsubscribe link
  (signed token, per the correction above) and updates `Campaign.DeliveredCount` as it progresses,
  setting `Status = Sent` (or `Failed` if the whole run errors, not per-recipient) on completion.
  `IJobScheduler` needs a new `EnqueueCampaignSend(Guid campaignId)` method, mirroring
  `EnqueueArtistInvite`'s enqueue-only (not scheduled-for-later) shape.
- `WithdrawMarketingOptInCommand(string SignedToken)` — `AllowAnonymous`, validates the signed
  token (new signer service, e.g. `IMarketingOptOutSigner`, same HMAC-SHA256 shape as the existing
  state signers — do not reuse `IInstagramStateSigner`/`ISocialOAuthStateSigner` directly, they're
  scoped to their own OAuth flows; a new signer with its own key is the established pattern for a
  new "prove identity without a session" use case in this codebase), sets `Client.MarketingOptIn
  = false`. Add the `architecture.md` `AllowAnonymous Exceptions` table row: `POST
  /api/v1/marketing/unsubscribe` | Anonymous unsubscribe link in campaign emails | Signed token
  (HMAC-SHA256) validated before trusting clientId, single studio+client pair per token.

### Frontend

- Owner: `/campaigns` page (gated in the UI on `Plan.AllowMarketingCampaigns`, matching whatever
  existing pattern gates `AllowApiAccess`-style features in the settings/billing UI — find and
  mirror it) — compose (subject/body/audience), draft/send, send history with delivered counts.
- A plain, unauthenticated unsubscribe confirmation page at whatever route
  `WithdrawMarketingOptInCommand`'s link target is (e.g. `/unsubscribe?token=...`) — simple
  "You've been unsubscribed" confirmation, no auth required, matching the endpoint's
  `AllowAnonymous` posture.

### Tests

- Unit: audience resolution respects `MarketingOptIn` in all three modes, including `Custom`
  (supplied client ids are still filtered); `Plan.AllowMarketingCampaigns` gate blocks `SendCampaignCommand`
  when false.
- Integration: full send flow (create draft → send → `SendCampaignJob` processes audience →
  `DeliveredCount`/`Status` updated); unsubscribe link flips `MarketingOptIn` to false and a
  second send afterward excludes that client.

### Help sync

- `owner-campaigns` help entry + manual section. No tour step (not a setup-checklist item, per
  the original spec).

---

## PHASE 3 — Plan Usage-Limit Enforcement, Completion (#19)

### Design decisions (pre-resolved)

**Not a from-scratch build.** Confirmed: `Artists`, `AppointmentsPerMonth`, and
`NotificationsPerMonth` are already gated via `IQuotaCheckedCommand`. `Locations` is deliberately
left unenforced — confirmed in `PlanLimitService.cs`, `QuotaType.Locations` always resolves to a
literal `1` with an explicit comment that this dimension "isn't modeled yet... always report 1 so
this dimension never blocks until [multi-location] ships." **Do not add enforcement for
`Locations`** — there's nothing to enforce against; multi-location is a separate, much larger,
explicitly-deferred feature (D14). This phase only completes the `StorageBytes` dimension.

**Correction to the original spec — the proposed approach doesn't fit this codebase's upload
architecture.** It says to "find every command that writes to R2... and add
`IQuotaCheckedCommand` with `QuotaType.StorageBytes` to the relevant upload commands." Verified:
file uploads in this codebase go through `GetPresignedUploadUrlQuery` (and its guest counterpart),
which mints a presigned direct-to-R2 PUT URL — **the backend never sees the file or its size**;
the client uploads the bytes straight to Cloudflare R2, bypassing the API entirely. There is no
"upload command" whose completion the backend observes, and no entity in this codebase stores an
uploaded file's size today (confirmed: no `SizeBytes`/`FileSizeBytes`/`ContentLength` field
exists anywhere in `Pena_e_Arte.Domain/Entities`). Gating the presign step itself can't check
"would this upload exceed the limit" because the size isn't known until after the direct-to-R2
PUT completes, by which point the backend was never involved again.

The correct mechanism, and the one this codebase already has the primitive for:
`IR2Service.ListByPrefixAsync(prefix, ct)` already exists and already returns each object's
`SizeBytes` (confirmed: used today by `GuestPendingUploadCleanupJob` for exactly this kind of
per-studio object listing). Build a **daily reconciliation job** that lists every object under
each studio's `{studioId}/` prefix, sums `SizeBytes`, and writes the total to
`Studio.StorageUsageBytes` (confirmed: this field is read by `PlanLimitService` today but never
written anywhere — this job is the first and only writer). `PlanLimitService.EnsureWithinLimitAsync`
already reads this field for the `StorageBytes` case — once the job writes real numbers instead
of the permanent `0` a never-written field defaults to, the existing quota-check logic starts
working with zero changes to `PlanLimitService` itself.

This means storage-quota enforcement is necessarily **eventual, not real-time** — a studio can
exceed its limit for up to a day before the next reconciliation run catches it and starts
blocking new presign requests. Document this explicitly as an accepted trade-off (this is a
common, reasonable pattern for storage quotas generally, and the alternative — tracking size at
upload time — would require either abandoning direct-to-R2 presigned uploads entirely or adding a
"confirm upload" round-trip to every single upload flow in the app, a far larger change than this
completion item warrants). Gate `GetPresignedUploadUrlQuery` (and the guest equivalent) with
`IQuotaCheckedCommand`/`QuotaType.StorageBytes` anyway, despite it being a query, not a state-
mutating command — the marker interface and `PlanLimitBehavior` pipeline check apply uniformly to
any MediatR request, and this stops a studio already over quota (per yesterday's reconciliation)
from minting further upload URLs, which is the best real-time enforcement actually available
here.

### Backend

- `StorageReconciliationJob` (new, `Pena_e_Arte.Infrastructure/Jobs/`) — mirror
  `GuestPendingUploadCleanupJob`'s use of `IR2Service.ListByPrefixAsync`, but instead of listing
  one fixed global prefix, iterate every active `Studio` and list by that studio's own
  `{studioId}/` prefix (the same per-tenant key-scoping `GetPresignedUploadUrlHandler` already
  uses when minting keys — confirmed: uploaded object keys are always prefixed
  `{tenant.StudioId}/...`, so summing by that prefix is exactly per-studio storage). Sum
  `SizeBytes` across all returned objects, write the total to `Studio.StorageUsageBytes`. Daily
  recurring registration (`IRecurringJobManager.AddOrUpdate`, continue whatever hour-stagger is
  latest in `Program.cs` by the time this merges — Group 4 used hour 7, Group 3 proposed hours 8
  and 9 for its own two new jobs; pick the next free hour, don't collide).
- Add `QuotaType.StorageBytes` to `GetPresignedUploadUrlQuery` and
  `GetPresignedGuestUploadUrlQuery` by implementing `IQuotaCheckedCommand` on both (confirming
  first that `PlanLimitBehavior`'s pipeline registration in `Program.cs` covers `IRequest<TResponse>`
  generically and isn't accidentally scoped to void/command-only requests — read that
  registration before assuming it "just works" for a query).
- Do not add anything for `QuotaType.Locations` — leave `PlanLimitService`'s existing
  always-return-`1` behavior exactly as it is.

### Frontend

- No new UI needed — the existing plan-usage display (wherever `GetUsageSnapshotAsync`'s response
  is already rendered, e.g. a billing/usage page) already shows the `StorageBytes` dimension; it
  will simply start showing real, non-zero numbers once the reconciliation job runs for the first
  time. Verify that existing display doesn't have a "coming soon"/placeholder note for storage
  that now needs removing.

### Tests

- Unit: `StorageReconciliationJob` sums sizes correctly per studio, doesn't cross-contaminate
  between studios' prefixes, handles a studio with zero objects (writes `0`, not skip).
- Integration: a studio at/over its `MaxStorageGb` limit (after reconciliation has run) gets a
  `PlanLimitExceededException` on the next presign request; a studio under its limit is
  unaffected.

### Help sync

- No new user-facing feature (this completes existing plan-usage enforcement); if the existing
  usage-display help copy mentions storage tracking as unavailable/estimated, update it to reflect
  that it's now enforced (daily-reconciled).

---

## Out of Scope — flagged explicitly, not silently dropped

- **SMS marketing campaigns**: explicitly deferred per the original spec's own guidance — needs
  a separate decision on Twilio A2P registration/consent-record requirements before that channel
  ships.
- **`Locations` quota enforcement**: deliberately not built — the dimension has no real feature to
  enforce against until multi-location (D14) ships; enforcing against a permanent `1` would be a
  no-op that looks like a real feature and isn't.
- **Real-time storage quota enforcement**: accepted as eventual (daily-reconciled), not
  synchronous, given the direct-to-R2 presigned-upload architecture — see Phase 3's design
  decision for the full reasoning.
- **Support Impersonation (#15)**: still not attempted — needs its own product/security
  conversation about the admin-endpoint allow-list, as flagged in the Group 3 prompt.

## Final self-check

- [ ] All three phases: `dotnet build` clean, `dotnet test` green (unit + integration).
- [ ] `pnpm tsc`/`pnpm lint`/`pnpm test` green.
- [ ] `pnpm build` clean.
- [ ] Every new `TenantEntity` has a migration and the standard global query filter.
- [ ] No new `ClientProfile.MarketingEmailOptOut`-style redundant field was added — Phase 2 uses
      `Client.MarketingOptIn` exclusively.
- [ ] The three-way (or four-way, once combined with Groups 3/4) discount stacking order in
      `CreateAppointmentCommand.cs` is documented in a single comment at the insertion point, not
      scattered across separate uncoordinated edits.
- [ ] `Plan.AllowMarketingCampaigns` actually blocks `SendCampaignCommand` when false — verified by
      a test, not just a hidden UI toggle.
- [ ] `StorageReconciliationJob` registered and its stagger hour doesn't collide with any
      existing daily job.
- [ ] No part of Support Impersonation (#15) was built.

## Final Deliverable

Append one Decisions Log entry per phase (three entries) to `docs/claude/architecture.md`,
matching the existing prose-paragraph entry format. Each entry notes: what was built, the product
decision it was built against (quoting the decision table at the top of this document, where
applicable), any correction made versus the original backlog spec, and verified test status. For
Phase 3, explicitly note the P1 backlog is now fully addressed except item #15.

Commit message:

```
feat: P1 backlog Group 5 — client referrals, marketing campaigns, storage quota completion (#4, #10, #19)

Builds the final three P1 backlog items now that the studio owner has resolved the open
referral-reward and marketing-consent questions: two-sided client referral rewards (percent off,
both referrer and referee), email-only marketing campaigns respecting the existing
Client.MarketingOptIn consent field (no redundant new field added), and completes plan
usage-limit enforcement for the storage dimension via a daily R2 reconciliation job (real-time
enforcement isn't possible given this codebase's direct-to-R2 presigned-upload architecture;
Locations remains deliberately unenforced pending multi-location support). Corrects the client-
referral spec's proposed Stripe-coupon reward mechanism, which only applies to the platform's own
Stripe-billed studio subscriptions, not Flow A client-facing payments. This closes the P1 backlog
audit from 2026-09-09 in full except Support Impersonation (#15), which remains pending a
separate product/security conversation about its admin-endpoint allow-list.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01S2ze276hAyTgmpbWntd3Kf
```
