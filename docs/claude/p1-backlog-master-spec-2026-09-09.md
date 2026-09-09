# P1 Backlog — Master Build Spec — 2026-09-09

> Source of truth: `industry-feature-parity-report-2026-07-20.md`'s consolidated
> backlog, re-verified against current source on 2026-09-09 (grep for the actual
> entities/endpoints, not just the report's prose — see
> `project_industry_feature_parity_audit` memory for what changed since 07-20:
> in-app messaging shipped separately (`overnight-prompt-in-app-messaging-2026-08-26.md`)
> and dropped off this list; plan usage-limit enforcement moved from 2/5 to 3/5
> dimensions and is now a narrower completion item, not a from-scratch build).
>
> **Purpose:** this file is the *input* to a master overnight prompt, not the
> prompt itself — it exists so whoever assembles that prompt (or runs a
> per-feature overnight session) has a concrete, current-codebase-accurate spec
> instead of re-deriving one from the stale July report. Each section is
> self-contained enough to lift directly into its own `overnight-prompt-<slug>.md`.
>
> **Conventions used throughout** (see `docs/claude/conventions.md`/`architecture.md`
> for the full rules — this is just the shorthand this doc assumes):
> - Every new entity extends `TenantEntity` (`Id`, `StudioId`, `CreatedAt`,
>   `UpdatedAt`, `DeletedAt`) unless explicitly noted as platform-wide (no
>   `StudioId`/no query filter, like `AuditLogEntry`).
> - MediatR command/query + FluentValidation validator in the same file
>   (`Pena_e_Arte.Application/<Feature>/Commands|Queries/`), thin Minimal API
>   endpoint in `Pena_e_Arte.API/Endpoints/<Feature>Endpoints.cs`, one of the five
>   canonical policies (`ClientOnly`, `ClientAndAbove`, `ArtistAndAbove`,
>   `OwnerOnly`, `AdminOnly`).
> - Mutating commands that should show up in "who changed what" implement
>   `IAuditableCommand` (`AuditAction`/`AuditTargetType`/`AuditTargetId` computed
>   properties, whitelist any useful metadata in `AuditMetadataBuilder`, no PII).
> - Frontend: one RTK Query slice per feature under `frontend/src/features/<feature>/`,
>   `createApi` + `builder.query`/`builder.mutation`, tag-based cache invalidation,
>   named hook exports. Role-gated routes added to `frontend/src/app/router.tsx`
>   under a `RoleGuard`.
> - **CLAUDE.md rule 7 is non-negotiable for every item below**: ship
>   `helpContent.ts` entries, the standalone manual (`frontend/public/user-manual/index.html`)
>   section, and any affected `frontend/src/features/help/tours/*Tour.ts` step in
>   the *same* change as the feature. Each spec below calls out which surfaces
>   it touches so this isn't an afterthought.
> - New EF Core migrations: run `dotnet ef migrations add <Name> --project Pena_e_Arte.Infrastructure`,
>   then strip the BOM the tool adds (see `feedback_windows_tooling_gotchas` —
>   CI's format-check rejects it) before committing.

---

## 1. Waitlist (A6 / C11 / D21 — guest, client, artist, owner surfaces)

**One data model, four consuming surfaces.** A guest/client joins a waitlist when
their preferred slot isn't available; the studio (owner/artist) sees who's
waiting; a cancellation triggers a notify hook.

**Data model:**
```csharp
public class Waitlist : TenantEntity
{
    public Guid? ArtistId { get; set; }          // null = any artist
    public Guid? ClientId { get; set; }           // null for a guest entry
    public string? GuestName { get; set; }
    public string? GuestEmail { get; set; }
    public string? GuestPhone { get; set; }
    public DateTime PreferredDateFrom { get; set; }
    public DateTime PreferredDateTo { get; set; }
    public WaitlistStatus Status { get; set; }     // Waiting, Notified, Booked, Expired, Cancelled
    public string? Notes { get; set; }
}
```
Mirror the guest-vs-account duality `CreateGuestAppointmentCommand`/
`CreateAppointmentCommand` already use — don't invent a second guest pattern.

**Backend:**
- `JoinWaitlistCommand` (`ClientId` resolved from `currentUser` when signed in,
  else guest fields required by validator) — `POST /api/v1/waitlist`,
  `AllowAnonymous` (mirrors the existing guest-booking `AllowAnonymous` exception
  documented in `architecture.md`), rate-limited like `/auth` endpoints.
- `GetMyWaitlistEntriesQuery` — `GET /api/v1/waitlist/mine`, `ClientAndAbove`.
- `GetWaitlistQuery` (studio-scoped list, filterable by artist) —
  `GET /api/v1/waitlist`, `ArtistAndAbove`.
- `CancelWaitlistEntryCommand` — owner-or-own-entry, `DELETE /api/v1/waitlist/{id}`,
  `ClientAndAbove` with the same 404-not-403 ownership pattern as
  `CancelAppointmentHandler`.
- **Notify hook**: in `CancelAppointmentHandler` (and the no-show/reschedule paths
  that free a slot), after the cancel succeeds, query `Waitlist` entries whose
  window covers the freed slot and artist, dispatch a new
  `SendWaitlistSlotAvailableNotificationCommand` per match (same
  `ISender.Send(new Send...Command(...))` pattern every other notification
  hook uses), and flip matched entries to `Notified`.
- `IAuditableCommand` on `CancelWaitlistEntryCommand` only if owner-cancelled
  (staff removing a client from the waitlist is a "who changed what" fact worth
  keeping; a client cancelling their own entry isn't, same asymmetry as
  appointment cancel not needing it either way — actually appointment cancel
  *does* audit both paths, so match that: audit both for consistency).

**Frontend:**
- Guest/client: "Notify me" CTA on `BookAppointmentForm.tsx` shown when
  `CheckSlotAvailabilityQuery` comes back unavailable; new
  `frontend/src/features/waitlist/` slice + `WaitlistEntryList.tsx` on the
  client's `/book` page (same card pattern as "My bookings").
- Artist/owner: waitlist queue view — new tab or card, reuse the `DataTable`
  pattern (`mobileCard` prop per the mobile UI/UX baseline work) rather than a
  raw table.

**Help sync:** `helpContent.ts` entries for `client-waitlist` and
`owner-waitlist` (or `artist-waitlist` if artists get their own queue view),
manual section, `clientTour.ts`/`ownerTour.ts` step pointing at the new UI.

**Open questions:** does "Notified" expire automatically (e.g. 24h to claim
before moving to the next person), and is notify-order FIFO or does the studio
manually pick? Needs a product decision before the notify-hook's matching logic
is final — building the entity+CRUD without the hook is not "done," don't ship
half of this.

---

## 2. Gift Cards (A7 / D10 — guest/client purchase, owner/D10 redemption)

**Data model:**
```csharp
public class GiftCard : TenantEntity
{
    public string Code { get; set; }              // unique, generated (e.g. 12-char base32)
    public decimal InitialBalance { get; set; }
    public decimal RemainingBalance { get; set; }
    public string PurchaserEmail { get; set; }
    public string? RecipientEmail { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public GiftCardStatus Status { get; set; }     // Active, Redeemed, Expired, Voided
}
```
`Code` needs a unique index scoped per-studio (or globally, per the open
question below).

**Backend:**
- `PurchaseGiftCardCommand` — creates a Stripe PaymentIntent for `InitialBalance`
  (reuse `IStripePaymentService`, same pattern as `CreateDepositPaymentCommand`),
  creates the `GiftCard` row in `Pending` until the webhook confirms payment
  (mirror `Payment`'s `PaymentStatus.Pending → Paid` webhook-driven transition —
  do **not** create the card as immediately redeemable before payment clears).
  `POST /api/v1/gift-cards`, `AllowAnonymous` + rate-limited (guest purchase is
  a first-party marketing feature, same posture as guest booking).
- `RedeemGiftCardCommand` — validates code + sufficient balance, decrements
  `RemainingBalance`, links against a `Payment` (new nullable
  `Payment.GiftCardId` FK + `Payment.GiftCardAmountApplied`). `POST
  /api/v1/gift-cards/redeem`, `ClientAndAbove`.
- `GetGiftCardBalanceQuery` — public lookup by code (for "check my balance"),
  `GET /api/v1/gift-cards/{code}/balance`, `AllowAnonymous`, rate-limited (this
  is an enumeration-risk endpoint — rate-limit hard, and don't leak
  `PurchaserEmail`/`RecipientEmail` in the response).
- `RedeemGiftCardCommand` implements `IAuditableCommand` (`Payment.Refunded`-tier
  financial event).

**Frontend:**
- Guest/client: gift-card purchase flow (new page/modal, Stripe Elements reuse
  from `PaymentMethodSelector.tsx`'s existing card-collection pattern) and a
  "Redeem gift card" field in the deposit/payment step of booking.
- Owner: gift-card list/lookup page under a new `/gift-cards` route
  (`OwnerOnly`), balance + status per card, void action.

**Help sync:** `client-gift-cards` (purchase + redeem) and `owner-gift-cards`
(issued-cards list) help entries, manual sections, no tour step needed
(secondary feature, not primary nav — same call the report made for A6/A7-style
additions).

**Open questions (blocking — do not build blind):**
- Breakage/expiry policy (do unredeemed balances ever revert to the studio, and
  after how long) — accounting/legal question.
- Refund liability: if a client disputes the *original* card payment via Stripe
  after the gift card's already been partially spent, who eats it?
- Balances studio-scoped (this repo's default posture, matches `TenantEntity`)
  or platform-wide (would need a `GiftCard` table outside the tenant model
  entirely, a bigger architectural change) — the report flagged this and it's
  still unresolved. Default to studio-scoped unless told otherwise; it's the
  cheaper build and matches every other entity in this codebase.

---

## 3. Packages / Prepaid Multi-Session Bundles (B10 / D9)

**Data model:**
```csharp
public class Package : TenantEntity          // the sellable product, owner-defined
{
    public string Name { get; set; }
    public int SessionCount { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
}

public class PackagePurchase : TenantEntity   // one client's instance of a Package
{
    public Guid PackageId { get; set; }
    public Guid ClientId { get; set; }
    public int SessionsRemaining { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public Guid PaymentId { get; set; }        // the Payment that bought it
}
```

**Backend:**
- `CreatePackageCommand`/`UpdatePackageCommand` (owner-defines-product CRUD,
  `OwnerOnly`) — straightforward, mirrors `DepositRule` CRUD shape.
- `PurchasePackageCommand` — Stripe PaymentIntent for `Package.Price`, creates
  `PackagePurchase` with `SessionsRemaining = Package.SessionCount` on payment
  success (webhook-driven, same caution as gift cards above).
- Extend `CreateAppointmentRequest` with an optional `PackagePurchaseId`; when
  present, `CreateAppointmentHandler` decrements `SessionsRemaining` instead of
  creating a new `Payment`/deposit flow — this is the one place this feature
  touches *existing* code, so it needs care: guard against
  `SessionsRemaining <= 0` with a `BusinessRuleViolationException`, and don't
  let a package-covered booking also trigger `DepositRule` logic (it's already
  paid for).

**Frontend:**
- Owner: `/packages` management page (`OwnerOnly`), same list+create+edit shape
  as `frontend/src/features/deposit-rules/`.
- Client: package purchase flow (parallels gift-card purchase), and a
  "Use a package" toggle in `BookAppointmentForm.tsx` that lists the client's
  active `PackagePurchase`s with remaining-session counts instead of the normal
  deposit step.

**Help sync:** `owner-packages`, `client-packages` help entries + manual
sections; add a step to `ownerTour.ts` only if packages become a primary
studio-setup action (probably not — treat like deposit rules, no tour step).

**Open questions:** unused-session expiry/refund policy on package
cancellation or a client leaving the studio — same class of question as gift
cards, needs a product decision, don't guess.

---

## 4. Client-to-Client Referral Program (B12)

Distinct from the existing platform-level `ReferralCode` (studio-to-studio
signup incentive) — do not extend that entity, it has a different shape and
audit trail already wired to issuer/admin flows.

**Data model:**
```csharp
public class ClientReferralCode : TenantEntity
{
    public Guid ReferrerClientId { get; set; }
    public string Code { get; set; }           // unique per studio
    public RewardType RewardType { get; set; } // PercentOffNextDeposit, FixedAmountOffNextDeposit
    public decimal RewardValue { get; set; }
    public int RedemptionCount { get; set; }
}

public class ClientReferralRedemption : TenantEntity
{
    public Guid ClientReferralCodeId { get; set; }
    public Guid RedeemedByClientId { get; set; }
    public Guid AppointmentId { get; set; }    // the booking the reward applied to
}
```

**Backend:**
- `GetOrCreateMyReferralCodeCommand` — auto-generates a code for a client on
  first request (idempotent), `ClientAndAbove`.
- Redemption hook in `CreateAppointmentCommand.cs`: accept an optional
  `ReferralCode` string on the booking request, look up an active
  `ClientReferralCode`, apply the reward to the resulting deposit calculation,
  record a `ClientReferralRedemption` row, increment `RedemptionCount`.
- Guard against self-referral (`ReferrerClientId == bookingClientId`) and
  re-redemption by the same client (one redemption per client per studio,
  enforced by a unique index on `(ClientReferralCodeId, RedeemedByClientId)` or
  simpler: unique on `(StudioId, RedeemedByClientId)` if a client can only ever
  redeem one referral ever at a given studio).

**Frontend:**
- Client: "Refer a friend" card (new component) showing their code + a share
  link, redemption count. Referral-code entry field added to
  `BookAppointmentForm.tsx` alongside the existing `ReferralSource` field
  (don't conflate the two — `ReferralSource` is "how did you hear about us"
  marketing attribution, this is a reward-bearing code).

**Help sync:** `client-referrals` help entry + manual section.

**Open questions (blocking):** reward mechanics — percent off vs fixed amount,
and crucially whether the *referrer* also gets a reward (two-sided) or only the
referee (one-sided). The platform-level referral system already has a
two-sided precedent (`overnight-prompt-two-sided-referral-rewards-2026-07-18.md`)
worth reading before assuming this one-sided — needs an explicit product
decision either way.

---

## 5. In-App Messaging (B13) — ~~STILL OPEN~~ ALREADY SHIPPED, remove from backlog

Confirmed via `git log -- Pena_e_Arte.Domain/Entities/Conversation.cs` (commit
`a0cdd6f`, `overnight-prompt-in-app-messaging-2026-08-26.md`):
`Conversation`/`ChatMessage` entities, `MessagingEndpoints.cs`,
`frontend/src/features/messaging/` (inbox, thread view, `useChatHub.ts` SignalR
integration) all exist and are wired up. **Do not rebuild this** — if it comes
up again in a future audit, check `git log` before trusting a "missing" verdict
from any report older than 2026-08-26.

---

## 6. Saved Payment Method (B15)

**Data model:** add `StripeCustomerId string?` to `ClientProfile` — mirrors the
existing `Studio.StripeCustomerId` pattern exactly, same nullable-until-first-use
shape.

**Backend:**
- Extend `IStripePaymentService` with a Setup Intent flow: create/reuse a
  Stripe Customer for the client on first deposit, create a `SetupIntent` for
  card-on-file, store the resulting default payment method reference.
- `CreateDepositPaymentCommand` gains an `off_session`/saved-card path: when the
  client has a `StripeCustomerId` and opts to reuse it, charge the saved
  payment method server-side instead of returning a client-side confirmation
  flow.
- New `DeleteSavedPaymentMethodCommand` (client removes their card),
  `ClientAndAbove`.

**Frontend:**
- `PaymentMethodSelector.tsx` gains a "Pay with saved card" one-click option
  when a saved method exists, alongside the existing card/cash choice.
- `My Profile` gets a "Payment methods" section to view/remove the saved card.

**Help sync:** update the existing `client-*` payment help entries (don't
create a whole new article if the deposit-payment one already covers this
territory — check `helpContent.ts` for the current `client-pay-deposit`-style
entry first and extend it).

**Open questions:** none per the original report — fully specified. The one
real risk is PCI scope creep: confirm the saved-card flow never touches raw
card data server-side (Stripe Elements + Setup Intents keeps this correct by
construction, don't deviate from that).

---

## 7. Installable PWA (B19)

Frontend-only, no backend/entity work.

**Build:**
- Add `vite-plugin-pwa` to `frontend/package.json` (only new npm package on
  this entire list that isn't already justified elsewhere — flag this
  explicitly if the project's "no new npm packages" constraint from the P0
  round applies here too; check with the user before adding if unsure).
- `manifest.json` (app name "TattooOS", icons, theme color matching the
  existing dark purple palette, `display: standalone`).
- Workbox service worker: cache the app shell + static assets for offline
  load; do **not** cache API responses (this app's data is real-time/
  multi-tenant sensitive — a stale cached appointment list is worse than a
  network-error state, and `verifier-gui`'s own notes already document that
  network-error states are correct/expected UI here).
- Register the service worker in `frontend/src/main.tsx`.

**Help sync:** none needed — this is infrastructure, not a UI surface with
settings to document (matches the report's own call on A10/A11).

**Open questions:** none — fully specified, native app explicitly out of
scope per the report.

---

## 8. Booth-Rent Tracking (C9)

**Data model:**
```csharp
// Add to Artist:
public decimal? CommissionRate { get; set; }   // nullable, percent

public class BoothRentSchedule : TenantEntity
{
    public Guid ArtistId { get; set; }
    public decimal AmountFixed { get; set; }
    public RentFrequency Frequency { get; set; }  // Weekly, Monthly
    public DateTime NextChargeDate { get; set; }
    public bool IsActive { get; set; }
}
```

**Backend:**
- `CreateBoothRentScheduleCommand`/`UpdateBoothRentScheduleCommand` — `OwnerOnly`
  CRUD.
- New recurring Hangfire job (`BoothRentChargeJob`, sibling to
  `AppointmentReminderJob`'s registration pattern in `Program.cs`) — on each
  `NextChargeDate`, auto-creates a `SessionSplit` line item labeled "Booth
  rent" against... this needs a `Payment` to attach to, and booth rent isn't
  tied to a specific appointment. Either (a) create a rent-only `Payment` row
  with `AppointmentId` nullable (a schema change to `Payment`, check current
  nullability first) or (b) model booth-rent charges as their own ledger
  table separate from `Payment`/`SessionSplit` entirely. **Resolve this schema
  question before implementing** — bolting it onto `SessionSplit` without
  confirming `Payment.AppointmentId` can be null is the likely first bug.
- Advance `NextChargeDate` by `Frequency` after each successful charge.

**Frontend:**
- Owner: booth-rent schedule management on the artist detail page (new
  section, next to the existing schedule/time-off editor from the C2/D2
  whitelist build).
- Artist: read-only view of their own rent schedule + history, on
  `/earnings` (natural home next to the earnings report from the P0 round).

**Help sync:** `owner-booth-rent` help entry + manual section, no tour step
(secondary financial config, same posture as deposit rules).

**Open questions:** whether rent charges attempt real Stripe collection
(auto-charge the artist's card) vs. bookkeeping-only (studio tracks what's
owed, settles out-of-band) — explicit product decision, the report flagged
this as unresolved and it still is. Bookkeeping-only is the safer default to
build first; real collection is a distinct, larger feature (needs the artist
to have their own payment method on file, an entirely separate concern from
B15's client-side saved cards).

---

## 9. Flash/Design Catalog (C13)

**Schema change — handle with care:** `Design.ClientId` is currently
non-nullable. Making it nullable is a real migration against production data;
confirm no other code path assumes `Design.ClientId` is always present before
touching this (grep every `.ClientId` access on `Design` first).

**Data model:** add `IsCatalogItem bool` and `Price decimal?` to `Design`.

**Backend:**
- `GetDesignCatalogQuery` — public, studio-scoped, filters
  `Design.IsCatalogItem == true`. `GET /api/v1/studios/{studioId}/design-catalog`
  or similar, `AllowAnonymous` (matches the guest-facing public-portfolio
  precedent already documented in `architecture.md`'s AllowAnonymous
  Exceptions section — add this as a new documented exception, don't leave it
  undocumented).
- `RequestCatalogDesignCommand` — the critical design decision from the report:
  this must create a **new appointment + a client-specific copy of the
  design**, never mutate the reusable catalog original. Concretely: clone the
  `Design` row (new `Id`, `ClientId` set, `IsCatalogItem = false`), clone its
  latest `DesignRevision`, then run the normal `CreateAppointmentCommand` flow
  linked to the clone. Get this wrong and every client who books the same
  flash piece corrupts each other's design thread.

**Frontend:**
- Artist: "Flash" tab on `ArtistPortfolioPage.tsx` (mark existing portfolio
  images / designs as catalog items, set price).
- Guest/client: flash browsing on the artist's public profile, "Book this
  design" CTA that pre-fills `BookAppointmentForm.tsx` with the catalog
  design's reference image and description.

**Help sync:** `artist-flash-catalog` help entry + manual section, tour step
on `artistTour.ts` if flash becomes a primary artist workflow (likely yes,
tattoo-vertical differentiator).

**Open questions:** none per the report — fully specified, but budget extra
review time for the clone-on-book logic given the data-corruption risk above.

---

## 10. Marketing Email/SMS Campaigns (D12)

**Data model:**
```csharp
public class Campaign : TenantEntity
{
    public string Subject { get; set; }
    public string BodyHtml { get; set; }        // email only at launch, see below
    public CampaignAudience Audience { get; set; } // AllClients, ClientsWithNoRecentVisit, Custom
    public CampaignStatus Status { get; set; }   // Draft, Sending, Sent, Failed
    public DateTime? SentAt { get; set; }
}
```

**Backend:**
- `CreateCampaignCommand`/`SendCampaignCommand` — `OwnerOnly`, gated behind a
  new `Plan.AllowMarketingCampaigns` flag (this repo just spent an entire
  overnight round hiding `AllowApiAccess`/`PrioritySupport` for being
  sold-but-undelivered flags — **do not repeat that mistake**: if this ships
  gated behind a plan flag, the flag must be real and enforced from day one,
  not a placeholder toggle).
- Fan-out job: Hangfire background job iterating the resolved audience,
  calling the existing `INotificationService.SendEmailAsync` per recipient
  (reuse, don't reinvent — this repo already has Resend wired for
  transactional email; a campaign send is just many transactional sends with
  rate limiting).
- Unsubscribe: every campaign email must include an unsubscribe link. Add
  `ClientProfile.MarketingEmailOptOut bool` (default false, opt-out not
  opt-in per most jurisdictions' existing-customer marketing rules — but flag
  this as a legal question, not an engineering one, given the EU-adjacent
  market noted throughout this codebase's other compliance decisions).
- `IAuditableCommand` on `SendCampaignCommand` — bulk-messaging real customers
  is exactly the kind of action D24's audit log exists to cover.

**Frontend:**
- Owner: `/campaigns` page — compose (subject/body/audience), draft/send,
  send history with delivery counts.

**Help sync:** `owner-campaigns` help entry + manual section, tour step
optional (not a setup-checklist item).

**Open questions (blocking — email-only first):** SMS campaign cost and
Twilio compliance (opt-in consent record, A2P registration implications) needs
a decision before that channel ships — the report is explicit that email-only
is the lower-risk starting point. Do not build SMS campaigns in the same pass
as email without that decision made.

---

## 11. Promo Codes / Discounts at Booking (D13)

Deliberately separate entity from platform `ReferralCode` and the new
`ClientReferralCode` (item 4) — three different code systems, don't conflate.

**Data model:**
```csharp
public class PromoCode : TenantEntity
{
    public string Code { get; set; }
    public RewardType RewardType { get; set; }   // reuse the enum from item 4 if shape matches
    public decimal RewardValue { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public int? MaxRedemptions { get; set; }
    public int RedemptionCount { get; set; }
    public bool IsActive { get; set; }
}
```

**Backend:**
- `CreatePromoCodeCommand`/`UpdatePromoCodeCommand` — `OwnerOnly` CRUD.
- Optional `PromoCodeId`/`PromoCode` string on `CreateAppointmentRequest`,
  applied in `CreateAppointmentCommand.cs` alongside (and after) any
  `ClientReferralCode` reward from item 4 — decide the stacking rule
  explicitly (are the two rewards additive, or does only one apply) rather
  than letting it fall out of implementation order by accident.

**Frontend:**
- Owner: `/promo-codes` management page, same shape as deposit rules.
- Client: promo code entry field in `BookAppointmentForm.tsx` (next to, but
  visually distinct from, the referral code field from item 4).

**Help sync:** `owner-promo-codes` help entry + manual section.

**Open questions:** none — fully specified per the report, but resolve the
stacking-with-referral-rewards question above before shipping both features
in overlapping timeframes.

---

## 12. Custom Intake-Form Fields (D22)

Today's intake "form" is a single freeform textarea
(`TattooDescription`/`SafetyNotes` on `Appointment`) — this replaces that with
an owner-configurable structured form, distinct from the existing
`ConsentTemplate` system (consent forms are legal-liability text with
versioning; intake forms are business-configurable structured data — don't
merge the two models even though they sound similar).

**Data model:**
```csharp
public class IntakeFormTemplate : TenantEntity
{
    public bool IsActive { get; set; }
    public string FieldSchemaJson { get; set; }   // JSON array of {label, type, required, options?}
}
```
Field types: `Text`, `Textarea`, `Select`, `Checkbox`, `Date` — keep the schema
minimal at launch, this is a form builder, not a general-purpose CMS.

**Backend:**
- `UpsertIntakeFormTemplateCommand` — `OwnerOnly`.
- `GetActiveIntakeFormTemplateQuery` — `ClientAndAbove`/`AllowAnonymous`
  (guest booking needs this too).
- Responses: add `Appointment.IntakeFormResponsesJson string?` (keyed by field
  label, matches the schema) rather than a normalized table — this mirrors
  how `SafetyNotes`/`TattooDescription` are already stored as flat fields, not
  over-engineered.

**Frontend:**
- Owner: intake-form builder page (`/intake-form-builder` or similar) —
  add/remove/reorder fields, set required/type.
- `SubmitIntakeFormPage.tsx` (and `BookAppointmentForm.tsx`'s freeform section)
  render the active template dynamically instead of the hardcoded textarea —
  this is the highest-risk part of this item, since it touches an existing,
  working, frequently-exercised flow. Budget a real browser QA pass across
  guest, client, and owner-configured-empty-template edge cases before calling
  this done.

**Help sync:** `owner-intake-form-builder` help entry + manual section, update
the existing `client-book-appointment` article's steps if the freeform-textarea
description no longer matches reality post-change.

**Open questions:** none — fully specified per the report.

---

## 13. CSV Data Export (D23)

Backend-only for the export mechanism; straightforward relative to everything
else on this list.

**Backend:**
- `GET /api/v1/exports/{entity}?format=csv` where `entity` is `clients`,
  `appointments`, or `revenue` — `OwnerOnly`, streams `text/csv` directly
  (don't buffer the whole export in memory for a studio with thousands of
  appointments; use a streaming `CsvWriter`-style approach, or at minimum
  paginate the underlying query).
- No new entity — this is a read-only projection over existing
  `Client`/`Appointment`/`Payment` data, tenant-scoped via the existing global
  query filters (no `IgnoreQueryFilters()` — this is an owner-only,
  own-studio export, unlike admin's cross-tenant audit log).

**Frontend:**
- "Export CSV" buttons on `ClientListPage.tsx`, the schedule/appointments
  view, and `ReportsPage.tsx` (the last one is a natural fit next to the P0
  round's new revenue-reporting UI).

**Help sync:** brief mention added to the existing owner list-page help
entries (clients, appointments, reports) rather than new standalone articles —
this is a small affordance on existing pages, not a new page.

**Open questions:** none — fully specified. This is a good candidate to build
before the heavier items on this list; low risk, immediate owner value.

---

## 14. Dunning / Failed-Payment Recovery Flow (E9)

**Backend:**
- `PastDueReminderJob` — Hangfire daily job, reusing the existing
  trial-warning job's pattern (find it via `grep -rn "TrialWarning" Pena_e_Arte.Infrastructure`
  before writing this from scratch — don't duplicate scheduling boilerplate).
  Escalating owner notifications at day 1/3/7 past due, via the existing
  `INotificationService`.
- Add `Subscription.DunningExcludedManually bool` (default false) — lets an
  admin exempt a cash/VIP account from the automated sequence without
  disabling dunning platform-wide. Toggle via a new admin command,
  `IAuditableCommand` (excluding a paying customer from collections is exactly
  a "who changed what" fact).
- Dashboard: add a `PastDueSince`-derived countdown banner (owner-facing,
  "Your payment is X days overdue") and a "days past due" sort column on the
  issuer's `SubscriptionOversightPage.tsx`.

**Frontend:**
- Owner: past-due banner on the dashboard/billing page.
- Admin: sort/filter by days-past-due on `SubscriptionOversightPage.tsx`,
  per-subscription dunning-exclusion toggle.

**Help sync:** `owner-past-due` help entry (what happens if payment fails,
what the banner means), admin manual section update for the exclusion toggle.

**Open questions:** none — fully specified per the report.

---

## 15. Support Impersonation with Audit Trail (E10)

**Now unblocked** — it was explicitly waiting on the structured audit log
(D24/E11), which is live since the P0 round (`AuditLogEntry` +
`IAuditableCommand` + `AuditLogBehavior`, confirmed working end-to-end
2026-09-09). Still has its own open product question below, so it's not
"ready to build blind" yet either.

**Data model:**
```csharp
public class ImpersonationSession : TenantEntity   // or platform-wide, no StudioId — see below
{
    public string ActorUserId { get; set; }        // the admin
    public Guid TargetStudioId { get; set; }
    public string ReasonCode { get; set; }
    public DateTime ExpiresAt { get; set; }         // time-boxed, short (e.g. 30-60 min)
    public DateTime? EndedAt { get; set; }
}
```

**Backend:**
- `StartImpersonationCommand` — `AdminOnly`, issues a short-lived JWT carrying
  an `imp:true` claim + the target studio's tenant id.
- Middleware/policy addition: any endpoint touching destructive actions,
  exports, or billing must explicitly reject tokens carrying `imp:true` — this
  is the allow-list question the report flagged as unresolved. Default posture:
  **deny by default**, allow-list read-only support-triage endpoints
  explicitly, rather than deny-list the dangerous ones (a missed deny-list
  entry is a real security bug; a missed allow-list entry is just an
  inconvenience for support). This is the single most important design
  decision in this item — get a product/security sign-off before writing the
  middleware, don't improvise it mid-implementation.
- Every request made under an active impersonation session should already hit
  the normal audit log via each individual command's `IAuditableCommand`
  wiring — `AuditLogBehavior` should additionally record `ActorRole` as
  something like `"admin-impersonating"` (or add an `ImpersonationSessionId`
  to `AuditLogEntry`) so impersonated actions are distinguishable from the
  admin's own actions after the fact. Check whether `AuditLogEntry` needs a
  schema change for this before assuming `ActorRole` alone is enough.

**Frontend:**
- Admin: "Impersonate" action on `IssuerStudioDetailPage.tsx`, reason-code
  prompt.
- Persistent, unmissable "Viewing as {studio}" banner at the layout root
  (visible on every page while impersonating, with an "End session" control) —
  this is a hard requirement, not a nice-to-have; an admin silently browsing
  as a studio with no visual indicator is the failure mode this whole feature
  exists to prevent.

**Help sync:** admin manual section only (this is not a studio-facing feature
studio users need documented).

**Open questions (blocking):** the allow-list scope decision above must be
made explicitly before implementation starts, not discovered via what breaks
in testing.

---

## 16. Timezone Handling (F14)

**Correctness gap, not just a feature gap** — worth prioritizing given
guest-artist/tattoo-tourism bookings across timezones are plausible for this
vertical, per the report.

**Data model:** add `Studio.Timezone string` (IANA identifier, e.g.
`"Europe/Lisbon"`), defaulted at studio onboarding (registration flow) based
on the studio's `Latitude`/`Longitude` if a reasonable IANA-lookup library is
already available, else default to a sane fallback and let the owner correct
it in settings.

**Backend audit (do this first, before writing any code):** confirm every
`DateTime` write path actually stores UTC. The report notes "the convention
exists in at least one query but isn't demonstrably enforced entity-wide" —
grep every `DateTime.Now` (should always be `DateTime.UtcNow`) across
`Pena_e_Arte.Application`/`Infrastructure` before touching the display layer;
fixing timezone *display* on top of inconsistent *storage* just moves the bug,
doesn't fix it.

**Frontend:** convert to studio-local time only at the display/notification
layer (appointment times shown to clients/artists, reminder email/SMS
content) — never at the storage or business-logic layer. This touches many
existing components; treat it as a systematic sweep (grep every
`.toLocaleString()`/raw `Date` render in `frontend/src/features/appointments/`
and `reports/`) rather than a single new feature surface.

**Help sync:** none needed (correctness fix, not a new user-facing setting
beyond the one new studio-settings field, which gets a one-line mention in the
existing studio-profile help entry).

**Open questions:** none for the mechanism — fully specified, but this is the
one item on this list that's a cross-cutting correctness sweep rather than a
bounded new feature, so scope/estimate it accordingly (likely larger than its
single backlog line suggests).

---

## 17. Booking Widget — Service/Style Selection Upfront (A3)

**Data model:** add `ServiceType`/`Style` (or reuse the existing
`TattooStyle` enum already used by `PortfolioImage.Style` — check for reuse
before adding a parallel enum) to `Appointment`.

**Backend:** extend `CreateAppointmentRequest`/`CreateGuestAppointmentCommand`
with the new optional field(s); no new entity, just a schema addition to an
existing hot-path command — run the full appointment test suite after this
change, it's exactly the kind of "small" field addition that breaks an
unrelated assertion elsewhere.

**Frontend:** `BookAppointmentForm.tsx` gains a structured Style `Select`
(prefilled from the chosen artist's `Specializations`, same data source
`ArtistDetailPage.tsx` already displays) instead of leaving style entirely
inside the freeform `TattooDescription` textarea.

**Help sync:** update the existing `client-book-appointment` help article's
steps to mention the new field.

**Open questions:** none — fully specified per the report.

---

## 18. Studio Structured Hours Field (A1)

**Feeds two other things**: the public studio-profile display (A1 itself) and
already-shipped D17 (`StudioClosure`, holiday/closure blocking) — this is the
recurring-weekly-hours counterpart to that one-off-date closure model, don't
conflate the two entities.

**Data model:**
```csharp
public class StudioHours : TenantEntity
{
    public DayOfWeek DayOfWeek { get; set; }
    public TimeSpan? OpenTime { get; set; }     // null = closed that day
    public TimeSpan? CloseTime { get; set; }
}
```
Mirrors `ArtistSchedule`'s shape closely — reuse that entity's patterns
(`UpsertArtistScheduleCommand`/`GetArtistScheduleQuery`) as the template rather
than designing from scratch.

**Backend:**
- `UpsertStudioHoursCommand`/`GetStudioHoursQuery` — same shape as the artist
  schedule endpoints, `OwnerOnly` for upsert, `AllowAnonymous` for the public
  read (feeds the public profile).
- Surface in `GetPublicStudioQuery`'s response + JSON-LD
  `openingHoursSpecification` (the report calls this out specifically — it's
  an SEO/structured-data requirement, not just a UI nicety).
- Feed into `CheckSlotAvailabilityQuery`: a booking request outside studio
  hours (even if the specific artist's own schedule would allow it) should be
  rejected — decide whether studio hours are a hard gate or just advisory
  before wiring this in, since `ArtistSchedule` already gates availability and
  stacking two gates incorrectly could produce confusing "no slots available"
  results with no visible reason.

**Frontend:**
- Owner: hours editor on `StudioProfilePage.tsx` (next to the existing
  `StudioClosuresCard.tsx` from the D17 build — same card, adjacent section,
  not a separate page).
- Public: hours display on the public studio profile page.

**Help sync:** `owner-studio-hours` help entry (or extend the existing
studio-profile article), manual section update, tour step on `ownerTour.ts`'s
setup sequence (this is exactly the kind of setup-checklist item the P0
round's `SetupChecklist.tsx` fix today made trustworthy again — consider
adding "Set your hours" as a third checklist item once this ships, so the
fix from PR #118 gets immediate real use).

**Open questions:** the hard-gate-vs-advisory question above needs resolving
during implementation, not left ambiguous — pick hard-gate as the default
(matches how `StudioClosure` already behaves) unless told otherwise.

---

## 19. Plan Usage-Limit Enforcement — Completion (E8)

**Not a from-scratch build — 3 of 5 dimensions are already enforced.**
Confirmed 2026-09-09: `CreateAppointmentCommand`, `CreateGuestAppointmentCommand`,
`CreateArtistCommand`, `CreateOwnArtistProfileCommand`, `AcceptStudioJoinInviteCommand`
implement `IQuotaCheckedCommand` for `Artists`/`AppointmentsPerMonth`, and
`CreateManualReminderCommand` covers `NotificationsPerMonth`. `PlanLimitService`
already computes usage for all 5 dimensions (`Storage`/`Locations` included) for
the admin-facing usage-display query — they're just not *gated* anywhere.

**Remaining work:**
- **Storage**: find every command that writes to R2 (portfolio images, design
  revisions, appointment attachments — grep for `IFileStorageService`/R2
  upload calls) and add `IQuotaCheckedCommand` with `QuotaType.StorageBytes`
  to the relevant upload commands. This is the harder of the two remaining
  dimensions since storage usage is computed from actual file sizes, not a
  simple row count — confirm `PlanLimitService`'s existing storage-usage
  calculation (line ~155 area, `QuotaType.StorageBytes` case) is accurate
  before trusting it as the enforcement source.
- **Locations**: per `architecture.md`'s existing note, `Plan.MaxLocations` is
  currently a vestigial field — true multi-location support doesn't exist yet
  (that's the separate, explicitly-not-a-quick-fix D14 item). Enforcing a
  `Locations` quota against a feature that doesn't exist is enforcing against
  a constant `1` — decide whether this dimension is even meaningful to gate
  before D14 ships, or whether it should stay usage-tracked-but-unenforced
  until multi-location is real. Likely answer: **defer this half of E8** until
  D14 lands; shipping "Locations: 1/1, cannot exceed" against a product that
  can't have more than 1 location anyway is enforcing a no-op.

**Reuse note from the original report, still true:** this pipeline-position
precedent (`IQuotaCheckedCommand` behind `PlanLimitBehavior`, cross-cutting
MediatR pipeline gated by a marker interface) is structurally identical to
`IAuditableCommand`/`AuditLogBehavior` — whoever picks this up should read
`AuditLogBehavior.cs` first as the closest working example of the same shape.

---

## Suggested build order (not a hard sequence, but respects dependencies)

1. **Low-risk, high-value, no open questions** — do these first: CSV export
   (13), booking widget style field (17), saved payment method (6), PWA (7).
2. **Foundational, feeds other items** — studio hours (18) feeds D17
   integration and the setup checklist; timezone (16) is a correctness sweep
   best done before more features add more `DateTime` call sites to audit
   later.
3. **Needs a product decision before implementation, not during** — waitlist
   (1, notify-order/expiry), gift cards (2, breakage/scope), packages (3,
   expiry), client referral (4, one-sided-vs-two-sided), campaigns (10,
   email-only-first is already decided, SMS is not), impersonation (15,
   allow-list scope), booth-rent (8, collection-vs-bookkeeping). Get answers
   before scheduling an overnight session for any of these — building the
   entity/CRUD shell without the decision just produces rework.
4. **Medium build, fully specified, no blockers** — flash catalog (9, but
   budget QA time for the clone-on-book risk), promo codes (11), custom
   intake fields (12, budget QA time for the existing-flow regression risk),
   dunning (14).
5. **Completion, not new-build** — plan usage-limit enforcement (19, storage
   dimension only; defer locations dimension until D14).
6. **Already done, remove from any future backlog list** — in-app messaging
   (5).

Cross-reference the "Do-not-build-blind list" from the P0 round's report
addendum before scheduling any of these: gift cards, packages, POS/inventory,
payroll/commission automation, multi-location, native mobile, SSO, i18n, tax
handling were all explicitly deferred as of 2026-07-21. Items 2 and 3 above
*are* on that list — this spec exists so that if/when the go-ahead comes, the
build isn't starting from zero, but the go-ahead itself is still a separate
decision this document does not make.

## Addendum — 2026-09-09, P1 Group 2 shipped

Items **18 (Studio Structured Hours Field)** and **16 (Timezone Handling)** —
Group 2 of the suggested build order above — shipped this pass. Moving both
out of the open backlog:

- **Item 18 (hours):** `StudioHours` entity + `{id}/hours` GET/PUT endpoints +
  hard gate in `ArtistAvailabilityExtensions`; `StudioHoursCard.tsx` on
  `/studios/me`; `openingHoursSpecification` JSON-LD + visible hours block on
  `StudioPortfolioPage.tsx`; default hours seeded at registration (both
  `RegisterStudioHandler` and `RegisterSoloArtistCommand`) and in `DataSeeder`.
- **Item 16 (timezone):** `Studio.Timezone` field + owner-editable select on
  `/studios/me`; `Pena_e_Arte.Application.Common.TimezoneUtils.ToStudioLocal`
  used in the three appointment-notification command handlers and in
  `AppointmentCard.tsx`/`AppointmentDetailPage.tsx`/`MyEarningsPage.tsx`.
- Full file list, and every deviation from this spec's own assumptions
  (`RegisterSoloArtistCommand` needing hours too; `EmailRenderer` left
  untouched in favor of converting once per handler; `SchedulePage.tsx` and
  `RevenueTrendChart.tsx` excluded from the timezone sweep; `MyBookingsSection.tsx`
  confirmed unreachable without a data-plumbing change): see
  `docs/claude/architecture.md`'s Decisions Log, "Studio structured hours +
  timezone handling — P1 Group 2 (2026-09-09)" entry.

**Precedent for later items:** `StudioHours` now being a real, enforced gate
(not just a display field) is something a later Group-3/4 item may want to
cite if it needs to reason about "when can a client interact with this studio
at all" — no such item currently does; noted here so a future spec doesn't
have to rediscover that this gate exists.

**New follow-up candidates identified during this pass** (not previously on
this backlog):

- A full UTC-storage audit beyond the cheap `DateTime.Now`-grep this spec's
  item 16 asked for — `DateTimeKind` coercion on MySQL/Pomelo reads, any raw
  SQL or migration-authored `DateTime` literals. The cheap check came back
  clean; a full read-path audit is a separate, more invasive verification pass.
- `MyBookingsSection.tsx`'s missing studio context — a client's own bookings
  list has no way to reach the studio's timezone per booking today
  (`GetMyAppointmentsQuery`/`AppointmentResponse` carry no studio field, and a
  client's appointments can legitimately span several studios). Needs its own
  data-plumbing design, not a quick fix.
- Per-viewer timezone display ("your appointment is 3pm studio time, 9am for
  you") — deliberately out of scope for this pass, which only fixed
  studio-local display; a materially larger feature if ever prioritized.
