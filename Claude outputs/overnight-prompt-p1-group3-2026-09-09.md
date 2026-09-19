# Overnight Master Prompt — P1 Backlog, Group 3 ("Decision-Gated")

**Date:** 2026-09-09
**Mode:** Autonomous overnight build. Work through the phases in order. Do not stop to ask
clarifying questions — every open design question below has been pre-resolved, either by
explicit product decision (noted per phase) or by live-codebase verification during this
session. If you hit a genuine blocker not covered here, stop that phase, document it in the
Decisions Log per the Final Deliverable section, and move to the next phase rather than
guessing silently.
**Run with:** a fresh branch off `main`, e.g. `feat/p1-group3-2026-09-09`.
**Before starting:** read `CLAUDE.md` in full (non-negotiable rules) and skim
`docs/claude/architecture.md`'s Decisions Log, the `IPaymentProvider`/`NullPaymentProvider`
entry, and the `AllowAnonymous Exceptions` table — three of the four phases below touch payment
flow or add a new anonymous endpoint.

## Context

This is the fifth master prompt executing the 2026-09-09 P1 backlog audit
(`docs/claude/audit-p1-backlog-2026-09-09.md` against
`docs/claude/p1-backlog-master-build-spec-2026-09-09.md`). Already shipped: Group 1 (CSV export,
booking style field, PWA), Group 2 (studio hours, timezone), Group 4 (promo codes, dunning,
custom intake fields, flash catalog). This prompt covers the four Group 3 items that were
originally decision-gated — each had at least one open product question the consultation
process could not resolve on its own. Those questions have now been answered explicitly by the
studio owner:

| Item | Question | Decision |
|---|---|---|
| Waitlist (#1) | Auto-FIFO notify vs. manual studio pick | **Auto-FIFO, 24h to claim** |
| Gift Cards (#2) | Do balances expire/revert to the studio | **Never expire** |
| Packages (#3) | Refund policy on unused sessions | **Non-refundable once purchased** |
| Booth-Rent (#8) | Auto-charge artist's card vs. bookkeeping-only | **Bookkeeping-only** |

A fifth Group 3 item — **Support Impersonation with Audit Trail (#15)** — is deliberately **not**
in this prompt. It is technically unblocked (the audit log it depended on shipped in the P0
round) but its remaining open question — which specific admin endpoints get allow-listed as safe
to use while impersonating a studio — needs a live product/security sign-off conversation, not a
quick-pick decision; scheduling it is a decision for the person running this repo, not something
resolved by this document. Do not build any part of it tonight.

**Build order:** Phase 1 (Waitlist) → Phase 2 (Booth-Rent) → Phase 3 (Gift Cards) → Phase 4
(Packages). Phases 3 and 4 both touch `CreateAppointmentCommand.cs`; build Gift Cards first so
Packages' purchase-flow code has an established in-repo pattern to copy rather than inventing its
own variant.

## Required reading before touching code

- `Pena_e_Arte.Application/Appointments/Commands/CancelAppointmentCommand.cs` — the exact hook
  point for the waitlist notify dispatch, and the existing refund-on-cancel logic Packages must
  NOT trigger for package-covered bookings.
- `Pena_e_Arte.Application/Appointments/Commands/CreateAppointmentCommand.cs` — shared booking
  core, needs a Packages-only change (see Phase 4).
- `Pena_e_Arte.Domain/Interfaces/IPaymentProvider.cs` and
  `Pena_e_Arte.Application/Payments/Commands/CreateDepositPaymentCommand.cs` — the **only**
  correct pattern for any new client-facing card payment in this codebase. Read the correction
  below before assuming the original backlog spec's wording is accurate.
- `Pena_e_Arte.Domain/Entities/Payment.cs` and
  `Pena_e_Arte.Infrastructure/Persistence/Configurations/PaymentConfiguration.cs` — confirms
  `Payment.AppointmentId` is non-nullable with a **database-enforced unique index**
  (`ux_payments_appointment_id`, one payment per appointment) and `Payment.ClientId` is also
  non-nullable. This directly resolves Phase 2's schema question — see below.
- `Pena_e_Arte.API/Extensions/RateLimitingExtensions.cs` — the five existing rate-limit policies
  (`auth`, `public-write`, `public-booking`, `public-read`, `billing`) with their exact
  limits/windows and partition-key convention. Use the existing policy names below; do not invent
  new ones without a documented reason.
- `frontend/src/shared/components/DataTable.tsx` — the `mobileCard` prop, required for every new
  list view below per the mobile UI/UX baseline.

## Constraints (apply to every phase)

- No new npm or NuGet packages.
- No `useEffect` for data fetching — RTK Query hooks only.
- TypeScript strict, no `any`.
- All business logic through MediatR commands/queries; no logic in endpoint lambdas beyond
  mapping.
- Every new endpoint: explicit `.RequireAuthorization("<Policy>")` or a documented
  `AllowAnonymous` exception added to the `architecture.md` table (Phase 1 and Phase 3 each need
  one — do not add either without the table row in the same commit, per CLAUDE.md hard rule).
  Rate-limit every new anonymous endpoint using one of the five existing policies.
- Every new entity needs an EF Core migration; every `TenantEntity` subclass gets the standard
  global query filter.
- Tests: unit tests for every new validator/handler; integration tests for every new endpoint,
  covering the happy path and the RBAC/tenant-isolation boundary.
- Help sync (CLAUDE.md rule #7): every user-facing addition updates `helpContent.ts`, the
  standalone manual (`frontend/public/user-manual/index.html`), and the relevant onboarding tour
  file in the same change, per each phase's own Help sync note (some are deliberately no-tour-step
  — don't add one where the phase says not to).

---

## PHASE 1 — Waitlist (#1)

### Design decisions (pre-resolved)

**Product decision (confirmed):** Auto-FIFO. When a slot frees up (cancellation, no-show, or
reschedule that frees the original slot), find `Waitlist` entries whose `ArtistId`
(null-or-matching) and preferred-date window cover the freed slot, order by `CreatedAt` (FIFO),
and notify **only the first match**, flipping it to `Notified` with a 24-hour claim window
(`NotifiedAt + 24h`). Do not notify the whole matching set at once — that would let multiple
people race for one slot. If the notified entry is not converted to a booking within 24 hours, a
new recurring job re-runs the same match against still-`Waiting` entries (see Backend) so the
next person in line gets their turn — this is the mechanism that makes "24h to claim" actually
expire rather than being a decorative timestamp.

### Backend

- `Pena_e_Arte.Domain/Entities/Waitlist.cs`:
  ```csharp
  public class Waitlist : TenantEntity
  {
      public Guid? ArtistId { get; set; }
      public Guid? ClientId { get; set; }
      public string? GuestName { get; set; }
      public string? GuestEmail { get; set; }
      public string? GuestPhone { get; set; }
      public DateTime PreferredDateFrom { get; set; }
      public DateTime PreferredDateTo { get; set; }
      public WaitlistStatus Status { get; set; }
      public DateTime? NotifiedAt { get; set; }
      public string? Notes { get; set; }
  }
  ```
  `WaitlistStatus`: `Waiting`, `Notified`, `Booked`, `Expired`, `Cancelled`. Migration
  `AddWaitlist`.
- `JoinWaitlistCommand` — `ClientId` resolved from `currentUser` when signed in, else guest
  fields required by validator (mirror `CreateGuestAppointmentCommand`'s guest-vs-account
  duality — do not invent a second guest pattern). `POST /api/v1/waitlist`, `AllowAnonymous`,
  rate-limited `public-booking` (same posture as guest booking submission — add the
  `architecture.md` table row: `POST /api/v1/waitlist` | Guest waitlist join, no prior account
  needed | Rate-limited (`public-booking`); no PII beyond what guest booking already collects).
- `GetMyWaitlistEntriesQuery` — `GET /api/v1/waitlist/mine`, `ClientAndAbove`.
- `GetWaitlistQuery` — studio-scoped list, filterable by artist, `GET /api/v1/waitlist`,
  `ArtistAndAbove`.
- `CancelWaitlistEntryCommand` — owner-or-own-entry, `DELETE /api/v1/waitlist/{id}`,
  `ClientAndAbove`, 404-not-403 on scope mismatch (mirror `CancelAppointmentHandler`'s exact
  pattern). `IAuditableCommand` on both staff- and client-initiated cancellation — matching
  `CancelAppointmentCommand`, which audits both paths for consistency (verified: its own comment
  explains why, copy that reasoning rather than re-deriving it).
- **Notify hook** — in `CancelAppointmentHandler` (`CancelAppointmentCommand.cs`), immediately
  after `appointment.Status = AppointmentStatus.Cancelled;` and before `await
  db.SaveChangesAsync(ct);`: query `Waitlist` entries with `Status == Waiting`, matching
  `ArtistId` (null or equal to the freed appointment's `ArtistId`) and whose
  `[PreferredDateFrom, PreferredDateTo]` window covers `appointment.Date`, ordered by
  `CreatedAt` ascending, take the first. If found: set `Status = Notified`, `NotifiedAt =
  DateTime.UtcNow`, and after the existing `await sender.Send(new
  SendAppointmentCancellationCommand(...))` line, dispatch a new
  `SendWaitlistSlotAvailableNotificationCommand(waitlistEntryId)` via the same `ISender.Send(new
  ...)` pattern every other notification hook in this handler already uses. Apply the identical
  hook to any other appointment-cancelling/slot-freeing path if one exists beyond
  `CancelAppointmentCommand` (grep for other `AppointmentStatus.Cancelled` assignments before
  assuming this is the only one).
- **Expiry job**: `WaitlistNotificationExpiryJob` (new,
  `Pena_e_Arte.Infrastructure/Jobs/`), daily recurring — mirror the
  `IRecurringJobManager.AddOrUpdate<T>(..., Cron.Daily(hour: N))` pattern (Group 4's Dunning
  phase added `PastDueReminderJob` at hour 7; use hour 8 here, continuing that stagger — check
  `Program.cs`'s current full stagger list before picking the hour, since Group 4 may not be
  merged yet in whatever order these prompts land). For every `Waitlist` row with `Status ==
  Notified && NotifiedAt < DateTime.UtcNow.AddHours(-24)`: set `Status = Expired`, then re-run the
  same FIFO match (next `Waiting` entry in line for that slot/artist) exactly as the cancel-hook
  above does, so the slot keeps cascading down the queue rather than dead-ending after one missed
  claim.
- When a client actually books the freed slot, the normal `CreateAppointmentCommand` flow has no
  awareness of the waitlist — add a small optional `WaitlistEntryId` hint on the booking request
  is unnecessary complexity; instead, mark the entry `Booked` from the client-facing "Notify me"
  flow itself (the client clicks through from a notification link that carries the waitlist entry
  id, lands on a pre-filled booking form, and on successful submission a thin
  `MarkWaitlistEntryBookedCommand` call — fired from the frontend alongside the booking submit —
  flips `Status = Booked`). Document this as a two-request sequence (book, then mark-booked) here
  so the implementer doesn't try to thread waitlist state through the shared booking core.

### Frontend

- Guest/client: "Notify me" CTA on `BookAppointmentForm.tsx`, shown when the existing
  slot-availability check comes back unavailable. New `frontend/src/features/waitlist/` slice +
  `WaitlistEntryList.tsx` on the client's `/book` page (same card pattern as "My bookings").
- Artist/owner: waitlist queue view, new tab or card using `DataTable`'s `mobileCard` prop (not a
  raw table — mobile baseline requirement).

### Tests

- Unit: FIFO match ordering, artist-null-matches-any, 24h expiry cascade (notified entry expires
  → next `Waiting` entry in the same window gets notified in its place).
- Integration: `JoinWaitlistCommand` guest and authenticated paths; `CancelWaitlistEntryCommand`
  RBAC/ownership boundary; cancel-appointment → waitlist-notify end-to-end.

### Help sync

- `client-waitlist` and `owner-waitlist` (or `artist-waitlist` if artists get their own queue
  view — check whether `ArtistAndAbove` on `GetWaitlistQuery` implies a distinct artist-facing
  page or shares the owner one before writing two help entries for one page) help entries, manual
  section, a `clientTour.ts`/`ownerTour.ts` step pointing at the new UI.

---

## PHASE 2 — Booth-Rent Tracking (#8)

### Design decisions (pre-resolved)

**Product decision (confirmed): bookkeeping-only.** No Stripe/`IPaymentProvider` call of any
kind — the system tracks what each artist owes on schedule; the studio settles it outside the
app. This resolves the original spec's flagged schema question cleanly: do **not** touch
`Payment` at all. Verified `Payment.AppointmentId` is non-nullable with a database-enforced
unique index (one payment per appointment) and `Payment.ClientId` is also non-nullable — booth
rent has neither an appointment nor a client (the artist owes the *studio*, the reverse direction
from every existing `Payment` row, which models client→studio money). Forcing booth rent through
`Payment` would mean relaxing that unique index and adding a nullable `ClientId`, a real schema
risk to an entity every other payment feature depends on, for no benefit now that no actual card
charge is involved. Build a dedicated ledger entity instead — smaller, safer, and a truthful model
of what this feature actually is (a bookkeeping record, not a payment).

### Backend

- Add to `Artist.cs`: `public decimal? CommissionRate { get; set; }` (nullable percent; confirmed
  `Artist` has no such field today — only `HourlyRate`). Migration `AddArtistCommissionRate`.
- `Pena_e_Arte.Domain/Entities/BoothRentSchedule.cs`:
  ```csharp
  public class BoothRentSchedule : TenantEntity
  {
      public Guid ArtistId { get; set; }
      public decimal AmountFixed { get; set; }
      public RentFrequency Frequency { get; set; }  // Weekly, Monthly
      public DateTime NextChargeDate { get; set; }
      public bool IsActive { get; set; }
  }
  ```
- `Pena_e_Arte.Domain/Entities/BoothRentCharge.cs` (new — the ledger, not `Payment`):
  ```csharp
  public class BoothRentCharge : TenantEntity
  {
      public Guid BoothRentScheduleId { get; set; }
      public Guid ArtistId { get; set; }
      public decimal Amount { get; set; }
      public DateTime ChargedDate { get; set; }
      public bool IsSettled { get; set; }          // owner marks paid-out-of-band
      public DateTime? SettledAt { get; set; }
      public string? SettledNote { get; set; }
  }
  ```
  Migration `AddBoothRentTracking` (both entities together).
- `CreateBoothRentScheduleCommand`/`UpdateBoothRentScheduleCommand` — `OwnerOnly` CRUD, mirror
  `DepositRule`'s CRUD shape.
- `BoothRentChargeJob` (new, `Pena_e_Arte.Infrastructure/Jobs/`) — daily recurring
  (`IRecurringJobManager.AddOrUpdate`, next free stagger hour after whatever Phase 1's
  expiry job took). For every active `BoothRentSchedule` with `NextChargeDate <=
  DateTime.UtcNow`: create a `BoothRentCharge` row (`Amount = AmountFixed`, `ChargedDate =
  NextChargeDate`, `IsSettled = false`), then advance `NextChargeDate` by `Frequency`. No payment
  provider call — this job only ever writes bookkeeping rows.
- `MarkBoothRentChargeSettledCommand` — `OwnerOnly`, sets `IsSettled = true`,
  `SettledAt`/`SettledNote`.
- `GetBoothRentChargesQuery` — studio-scoped, filterable by artist, `ArtistAndAbove` (an artist
  can see their own; check whether the query needs an explicit `ArtistId == callingArtist.Id`
  filter when the caller's role is `artist`, matching how other artist-self-scoped queries in
  this codebase already restrict by resolved `Artist.Id`, not just by role).

### Frontend

- Owner: booth-rent schedule management on the artist detail page (new section, next to the
  existing schedule/time-off editor).
- Artist: read-only view of their own rent schedule + charge history, on `/earnings` (natural
  home next to the existing earnings report).

### Tests

- Unit: `BoothRentChargeJob` — creates exactly one charge per due schedule per run, advances
  `NextChargeDate` correctly for both `Weekly`/`Monthly`, does not double-charge on a second run
  the same day.
- Integration: `MarkBoothRentChargeSettledCommand` — `OwnerOnly` enforced. `GetBoothRentChargesQuery`
  — artist sees only their own charges, owner sees all.

### Help sync

- `owner-booth-rent` help entry + manual section. No tour step (secondary financial config, same
  posture as deposit rules).

---

## PHASE 3 — Gift Cards (#2)

### Design decisions (pre-resolved)

**Product decision (confirmed): balances never expire.** Drop `GiftCard.ExpiresAt`/expiry logic
from the original spec entirely — no breakage/reversion-to-studio behavior to build. This also
sidesteps the original spec's "who eats a post-spend chargeback" question; that's a support/ops
risk-acceptance question, not a schema question, and doesn't block building the feature (see Out
of Scope).

**Balances are studio-scoped** (a `TenantEntity`, `Code` unique per studio, not platform-wide) —
this was the original spec's own stated default when no other instruction is given, and it's the
cheaper build that matches every other entity in this codebase; adopted without needing a
separate product decision.

**Correction to the original spec.** It says to "reuse `IStripePaymentService`, same pattern as
`CreateDepositPaymentCommand`." `IStripePaymentService` **does not exist** — it was deleted
(not migrated) on 2026-07-31 for the Article 4(g)/Amendment A compliance reason documented in
`IPaymentProvider.cs`'s own doc comment. The correct interface is `IPaymentProvider`, and its
`NullPaymentProvider` implementation is the current DI default, which fails closed until a
replacement provider ("POK") lands. **This is not the same blocker Group 1's item #6 (Saved
Payment Method) hit** — that item needed persistent stored-card capability with no slot in the
current abstraction at all. Gift card purchase is a single one-time auth-hold-then-capture,
exactly the shape `IPaymentProvider.CreatePaymentHoldAsync`/`CaptureAsync` already models and
exactly the shape `CreateDepositPaymentCommand` already uses successfully today (deposits are
live in this codebase right now, running against `NullPaymentProvider`, i.e. currently
non-functional in production for the same reason, by the same design — this is expected,
accepted, existing behavior, not a new gap). Build gift-card purchase as a structural clone of
`CreateDepositPaymentCommand`'s provider-interaction pattern (auth hold → webhook/reconciliation
confirms → capture), and it will light up the moment POK lands, with no gift-card-specific
follow-up work needed. Do not gate the UI on anything beyond the existing `GET
/api/v1/payments/capabilities` capability check the deposit-checkout flow already uses — mirror
that, don't invent a second capability-detection mechanism.

Use the same hardcoded `"EUR"` currency argument `CreateDepositPaymentCommand` passes to
`CreatePaymentHoldAsync` (yes, this is inconsistent with `Payment.Currency`'s own `"ALL"`
default — that's pre-existing behavior in the deposit flow already, not something this phase
should "fix" as a side effect; match it for consistency, don't diverge).

### Backend

- `Pena_e_Arte.Domain/Entities/GiftCard.cs`:
  ```csharp
  public class GiftCard : TenantEntity
  {
      public string Code { get; set; } = string.Empty;   // unique per studio, e.g. 12-char base32
      public decimal InitialBalance { get; set; }
      public decimal RemainingBalance { get; set; }
      public string PurchaserEmail { get; set; } = string.Empty;
      public string? RecipientEmail { get; set; }
      public GiftCardStatus Status { get; set; }          // Pending, Active, Redeemed, Voided
  }
  ```
  No `ExpiresAt` (never-expire decision above). `Status` starts `Pending`, becomes `Active` only
  once the provider confirms payment (mirror `Payment`'s `Pending → Paid` webhook-driven
  transition exactly — do not create the card as usable before payment clears). Migration
  `AddGiftCards`, unique index on `(StudioId, Code)`.
- Extend `Payment` with `Guid? GiftCardId` (nullable — most `Payment` rows are still
  appointment-deposit rows) — actually: **do not extend `Payment` for this.** `Payment.AppointmentId`
  is non-nullable with a unique-per-appointment index; a gift-card purchase has no appointment at
  all, so it cannot be a `Payment` row any more than booth rent could (Phase 2's reasoning
  applies equally here). Give `GiftCard` its own
  `ProviderReferenceId`/`ClientSecret`/`Provider` fields directly (same three fields
  `Payment` already carries for exactly this purpose — copy them onto `GiftCard`, don't share
  the table). `RedeemGiftCardCommand`'s balance-linking to an actual appointment payment (below)
  is a separate, later step from the purchase itself.
- `PurchaseGiftCardCommand` — `CreatePaymentHoldAsync(amountInCents, "EUR", giftCardId, ct)`
  exactly mirroring `CreateDepositPaymentCommand`'s call shape, `GiftCard.Status = Pending` until
  confirmed. `POST /api/v1/gift-cards`, `AllowAnonymous`, rate-limited `public-booking` (guest
  purchase, same posture as guest checkout) — add the `architecture.md` table row.
- Reconciliation: whatever mechanism currently flips a `Payment` from `Pending`/`Captured` to
  `Paid` (`PaymentReconciliationJob` and/or a webhook handler — check both before assuming only
  one path exists) needs the equivalent for `GiftCard`. Read `PaymentReconciliationJob.cs` in
  full before writing this — if it's written generically enough to extend for a second
  provider-backed entity, extend it; if not, add a small parallel job rather than overloading
  `Payment`-specific logic with a `GiftCard` branch.
- `RedeemGiftCardCommand` — validates code + `Status == Active` + `RemainingBalance >= requested
  amount` (redemption amount is whatever the client applies at checkout, up to the appointment's
  deposit — do not require full-balance redemption in one shot), decrements `RemainingBalance`,
  applies the redeemed amount as a deduction to the target appointment's deposit exactly like
  Group 4's `PromoCode` discount does (same insertion point in `CreateAppointmentCommand.cs`,
  after `DepositCalculator.Calculate(...)` — apply promo code first, then gift card, on whatever
  remains; document the exact order since both can theoretically apply to one booking).
  `POST /api/v1/gift-cards/redeem`, `ClientAndAbove`. `IAuditableCommand` (financial event, same
  tier as a refund).
- `GetGiftCardBalanceQuery` — public lookup by code, `GET /api/v1/gift-cards/{code}/balance`,
  `AllowAnonymous`, rate-limited `public-read` — this is an enumeration-risk endpoint (someone
  could brute-force codes); do not return `PurchaserEmail`/`RecipientEmail` in the response, only
  `RemainingBalance`/`Status`. Add the `architecture.md` table row noting the enumeration-risk
  rate-limit reasoning explicitly, matching how the existing table entries document *why* each
  anonymous read is safe.

### Frontend

- Guest/client: gift-card purchase flow (new page/modal, reuse whatever Stripe Elements /
  card-collection component the deposit-checkout flow already uses — find it via
  `DepositCheckoutPage.tsx`, don't build a second card-input component) and a "Redeem gift card"
  field in the deposit/payment step of booking, visually distinct from Group 4's promo-code field
  (both can be present on the same form).
- Owner: gift-card list/lookup page under `/gift-cards` (`OwnerOnly`), balance + status per card,
  void action (`VoidGiftCardCommand`, `OwnerOnly`, `IAuditableCommand` — not in the original
  spec's command list but implied by "void action" in its frontend section; add it since the
  frontend bullet requires a backend counterpart).

### Tests

- Unit: redemption math (partial redemption leaves correct `RemainingBalance`, cannot redeem more
  than remaining, cannot redeem a `Voided`/`Pending` card). Purchase does not activate the card
  before provider confirmation.
- Integration: `GetGiftCardBalanceQuery` never leaks purchaser/recipient email; rate-limit applied;
  redemption combined with a promo code applies both discounts in the documented order.

### Help sync

- `client-gift-cards` (purchase + redeem) and `owner-gift-cards` (issued-cards list, void) help
  entries, manual sections. No tour step (secondary feature, same posture the original spec
  called for).

---

## PHASE 4 — Packages / Prepaid Multi-Session Bundles (#3)

### Design decisions (pre-resolved)

**Product decision (confirmed): non-refundable once purchased.** Drop any refund/cancellation
payout logic for unused `PackagePurchase` sessions — a client who leaves with sessions remaining
simply forfeits them. This substantially simplifies the original spec's open question; there is
no refund path to design.

**Same `IPaymentProvider` correction as Phase 3 applies here** — the original spec also says
"Stripe PaymentIntent," meaning `IPaymentProvider`/`CreatePaymentHoldAsync`, not the deleted
`IStripePaymentService`. Build `PurchasePackageCommand` as the same structural clone of
`CreateDepositPaymentCommand` that Phase 3's `PurchaseGiftCardCommand` is — by this phase, that
pattern already exists once in this branch (Phase 3), so copy from the Phase 3 code, not from
`CreateDepositPaymentCommand` a second time independently, to keep the two provider-integration
call sites textually consistent.

### Backend

- `Pena_e_Arte.Domain/Entities/Package.cs`:
  ```csharp
  public class Package : TenantEntity
  {
      public string Name { get; set; } = string.Empty;
      public int SessionCount { get; set; }
      public decimal Price { get; set; }
      public bool IsActive { get; set; }
  }
  ```
- `Pena_e_Arte.Domain/Entities/PackagePurchase.cs`:
  ```csharp
  public class PackagePurchase : TenantEntity
  {
      public Guid PackageId { get; set; }
      public Guid ClientId { get; set; }
      public int SessionsRemaining { get; set; }
      // No ExpiresAt — non-refundable/no-expiry decision means there is nothing to enforce
      // against a session count that only ever decrements; if a future session wants a
      // separate "package validity window" that's a distinct product question, not implied
      // by tonight's decision.
      public string ProviderReferenceId { get; set; } = string.Empty; // mirrors GiftCard's own field, not Payment's
      public string? ClientSecret { get; set; }
  }
  ```
  Migration `AddPackages`. `SessionsRemaining = 0` and no `Status` enum needed — a `Package`
  purchase either exists (payment confirmed, decided via the same reconciliation mechanism Phase
  3 built/extended) or doesn't get created at all until confirmation, matching `GiftCard`'s
  `Pending`-until-confirmed posture but without needing an explicit status field here since there
  is no "voidable" concept for a package the way there is for a gift card.
- `CreatePackageCommand`/`UpdatePackageCommand` — `OwnerOnly` CRUD, mirror `DepositRule` CRUD
  shape.
- `PurchasePackageCommand` — same provider-call shape as Phase 3's `PurchaseGiftCardCommand`;
  creates `PackagePurchase` with `SessionsRemaining = Package.SessionCount` only once payment is
  confirmed (webhook/reconciliation-driven, same caution as Phase 3).
- Extend `CreateAppointmentRequest` with an optional trailing `Guid? PackagePurchaseId` (append
  after Group 4's `PromoCode` field if that's already landed in this branch, else append at the
  end — positional record, do not insert mid-list). In `CreateAppointmentCoreAsync`, when
  `req.PackagePurchaseId` is present: load the `PackagePurchase`, verify `ClientId` matches the
  booking client and `SessionsRemaining > 0` (throw `BusinessRuleViolationException` if not),
  decrement `SessionsRemaining`, and **skip** the `DepositCalculator.Calculate(...)`/promo/gift-card
  discount block entirely — a package-covered booking is already paid for, so `depositAmount`
  should be set to `0` and `DepositStatus` to a value indicating pre-paid (check whether
  `DepositStatus` enum already has a suitable member, e.g. something used for
  fully-comped/pre-paid bookings, before adding a new enum value — if none fits, add
  `DepositStatus.PrePaid` and confirm every existing switch/exhaustive-match over `DepositStatus`
  in the codebase handles the new member, since C# won't warn on a missing enum arm in a
  non-exhaustive switch).

### Frontend

- Owner: `/packages` management page (`OwnerOnly`), same list+create+edit shape as the deposit
  rules management page.
- Client: package purchase flow (parallels the gift-card purchase page from Phase 3), and a "Use
  a package" toggle in `BookAppointmentForm.tsx` listing the client's active `PackagePurchase`s
  with remaining-session counts, replacing the normal deposit step when selected.

### Tests

- Unit: package-covered booking decrements `SessionsRemaining` exactly once, rejects a booking
  when `SessionsRemaining <= 0`, does not apply `DepositRule`/promo/gift-card logic when a package
  is used.
- Integration: `PurchasePackageCommand` payment-confirmation flow (mirrors Phase 3's tests
  structurally); booking with a package produces `DepositAmount == 0` and the correct
  `DepositStatus`.

### Help sync

- `owner-packages`, `client-packages` help entries + manual sections. No tour step (same posture
  as deposit rules, per the original spec).

---

## Out of Scope — flagged explicitly, not silently dropped

- **Gift card post-spend chargeback liability**: who absorbs the loss if a client disputes the
  original gift-card purchase charge after the balance has already been redeemed. This is a
  support/ops risk-acceptance policy, not a schema or code question, and doesn't block building
  the feature — revisit only if it becomes an actual incident.
- **Package validity window**: no expiry on unused sessions was requested or built; if a future
  product decision wants one, it's a new field and a new enforcement point, not implied by
  tonight's "non-refundable" decision.
- **Booth-rent auto-charge**: explicitly deferred — tonight builds bookkeeping-only. If the studio
  later wants real card collection from artists, that's a distinct, larger feature needing artists
  to have their own payment method on file (separate from the client-side saved-card concept
  Group 1 also declined to build for the same underlying `IPaymentProvider`-abstraction reason).
- **Support Impersonation (#15)**: not attempted tonight at all — needs a dedicated
  product/security conversation about the admin-endpoint allow-list before any code is written.
- **Waitlist notification channel**: this document assumes email (via the existing
  `SendWaitlistSlotAvailableNotificationCommand` pattern, matching every other notification hook);
  if SMS is also wanted for time-sensitive slot claims, that's a `NotificationChannel` extension,
  not assumed here.

## Final self-check

- [ ] All four phases: `dotnet build` clean, `dotnet test` green (unit + integration).
- [ ] `pnpm tsc`/`pnpm lint`/`pnpm test` green.
- [ ] `pnpm build` clean.
- [ ] Every new `TenantEntity` has a migration and the standard global query filter.
- [ ] `Payment` entity itself was **not** modified by Phase 2 or Phase 3 (both deliberately use
      their own dedicated fields/entities instead) — confirm no stray edit crept in.
- [ ] Both new `AllowAnonymous` endpoints (Phase 1's waitlist join, Phase 3's gift-card purchase
      and balance lookup — three endpoints total) have their `architecture.md` table rows.
- [ ] `GetGiftCardBalanceQuery`'s response never includes `PurchaserEmail`/`RecipientEmail`.
- [ ] Help sync completed for every user-facing addition.
- [ ] No part of Support Impersonation (#15) was built.

## Final Deliverable

Append one Decisions Log entry per phase (four entries) to `docs/claude/architecture.md`,
matching the existing prose-paragraph entry format (no bullet lists inside an entry). Each entry
notes: what was built, the product decision it was built against (quoting the decision table at
the top of this document), any correction made versus the original backlog spec, and verified
test status.

Commit message:

```
feat: P1 backlog Group 3 — waitlist, booth-rent, gift cards, packages (#1, #8, #2, #3)

Builds the four decision-gated Group 3 items now that the studio owner has resolved each item's
open product question: waitlist auto-FIFO with 24h claim windows, booth-rent tracked as
bookkeeping-only (no card charges), gift cards that never expire, and non-refundable prepaid
packages. Corrects the original spec's payment-integration references from the deleted
IStripePaymentService to IPaymentProvider (matching the existing deposit-payment pattern), and
resolves the booth-rent/gift-card schema question by giving both their own dedicated
entities/fields rather than extending Payment (which has a database-enforced one-payment-per-
appointment unique index that neither feature fits). Support Impersonation (#15) remains
unbuilt pending a separate product/security conversation about its admin-endpoint allow-list.

Co-Authored-By: Claude Sonnet 5 <noreply@anthropic.com>
Claude-Session: https://claude.ai/code/session_01S2ze276hAyTgmpbWntd3Kf
```
