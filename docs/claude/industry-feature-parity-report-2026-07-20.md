# Industry Feature-Parity Report — 2026-07-20

> Audit branch: `audit/industry-feature-parity-2026-07-20`. Methodology defined in
> `overnight-prompt-industry-feature-parity-audit-2026-07-20.md`. This file is the
> completed findings report that prompt calls for.

## Market research summary

The vertical booking-SaaS category (Vagaro, Fresha, Boulevard, Mindbody, Zenoti,
GlossGenius, Booksy, Mangomint, Schedulicity, Square Appointments) has converged on a
common baseline that Pena e Artë already matches on scheduling, deposits, and reviews,
but diverges from on commerce and retention tooling. Every platform researched supports
self-service waitlists (Vagaro), deposit/no-show protection with configurable fees
(Booksy, Fresha), and some form of prepaid packages, gift cards, or memberships (Vagaro's
"Online Shopping Cart"; Fresha's 2026 membership overhaul; Boulevard's subscription-style
memberships). Client self-service reschedule is a baseline expectation — Boulevard leads
with "Precision Scheduling" surfacing best-fit slots — and checkout increasingly bundles
tipping, saved cards, and same-day payout (GlossGenius, Mangomint's "Client Self
Checkout" pay-by-text link). Staff-facing tooling has matured too: Vagaro's "Automatic
Rent & Fees" treats booth rent as a first-class recurring object, and Mangomint now runs
full in-platform payroll with tax filing. Marketing/AI tooling (GlossGenius "AI Analyst",
Fresha's AI receptionist, Booksy's paid marketplace "Boost") is the newest wave and is
often gated to premium tiers even among competitors — useful context for rating these P1
rather than P0. Tattoo-specific software (Venue Ink, Porter, Tattoo Studio Pro, InkDesk)
remains a fragmented, sub-$500M market with no dominant leader; its distinctive tooling —
native consent forms, per-artist booth-rent tracking, flash-catalog browsing — is
precisely where Pena e Artë already has genuine, shipped differentiators (consent forms,
design-approval workflow, body map, portable cross-studio profile) that generic salon
software still bolts on as workarounds.

On the platform-admin side (the issuer role's own competitive set), 2026 B2B SaaS norms
center on protecting revenue and trust once real money and real businesses are on a
platform: Stripe's Smart Retries is the de facto dunning baseline (free, ML-timed, ~8
retries over 2 months), support impersonation is expected to ship with an immutable,
role-gated, time-boxed audit trail, and a structured queryable admin-action log is treated
as near-compliance-mandatory once suspend/cancel/plan-edit actions touch paying customers.
Pena e Artë's issuer surface is strong on the subscription-lifecycle side (plan CRUD,
trial extension, manual cash activation, referral management) but has none of this
protective/trust layer built yet — a materially different kind of gap than the vertical
feature gaps above, since it exposes the platform's own operators to risk, not just a
competitive-feature shortfall against tattoo-studio-facing competitors.

Accessibility expectations are unified across surfaces in 2026: W3C's WCAG2Mobile note
folds native, mobile-web, and PWA experiences into a single WCAG 2.2 conformance target —
there is no separate "mobile-only" bar. ADA Title II sets WCAG 2.1 AA as the practical
compliance floor for covered entities by 2026-05-11 (orgs with 15+ employees), a
reasonable minimum even for a private-sector SaaS in this category. Every competitor
researched ships a native companion app rather than relying on PWA-only delivery, which is
the single largest infrastructure gap identified below (Pena e Artë has neither).

*(Full source citations retained in the research agent's working notes; not reproduced
here to keep this report focused on verdicts.)*

---

## Section A — Guest

| # | Feature | Verdict | Priority | Evidence | Recommendation |
|---|---|---|---|---|---|
| A1 | Public studio profile (photos, hours, location, reviews) | PARTIAL | P1 | `Studio.cs` has no hours/schedule field; `GetPublicStudioQuery.cs` response has no hours | Add `StudioHours` on `Studio`, surface in response + JSON-LD `openingHoursSpecification` |
| A2 | Public artist portfolio (samples, specialties, pricing) | PARTIAL | P2 | `PortfolioImage.cs` has no price field; only single `Artist.HourlyRate` shown | Add optional per-piece `Price`/`PriceRange` |
| A3 | Booking widget — service/style selection upfront | MISSING | P1 | `Appointment.cs` has no Service/Style field; `BookAppointmentForm.tsx` is duration-only, style captured as free text | Add `ServiceType`/`Style` to booking request; structured Select prefilled from artist's `Specializations` |
| A4 | Map/"studios near me" discovery | PRESENT | — | `GetNearbyStudiosQuery.cs`, `StudioMapPage.tsx`, `DiscoverPage.tsx` | — |
| A5 | Review aggregation (Google/Instagram sync vs first-party) | PRESENT (first-party only) | P2 | No GBP/Instagram review-sync code found | Legitimate gap vs aggregator platforms, not a defect at this stage |
| A6 | Waitlist / "notify me if slot opens" | MISSING | P1 | No `Waitlist` entity anywhere | See backlog |
| A7 | Gift cards purchasable by public | MISSING | P2 | No `GiftCard` entity/endpoint | See backlog |
| A8 | Group/party booking | N/A | — | `Appointment.cs` strictly 1 artist + 1 client; tattoo sessions are inherently single-client | Confirmed N/A |
| A9 | Social proof (verified badge, "X bookings this month") | PARTIAL | P2 | `IsVerifiedBooking` badge is rendered (`ReviewSection.tsx`); no trending/popularity signal | Low priority; important half already shipped |
| A10 | SEO: sitemap.xml, robots.txt, structured data | PARTIAL | P1 | No sitemap/robots found; JSON-LD/canonical/meta confirmed present | **Built tonight** — see below |
| A11 | Cookie consent/privacy banner | MISSING | P1 | No cookie/consent/GDPR code anywhere | **Built tonight** — see below |
| A12 | "Powered by" platform branding | PRESENT | — | `Studio.ShowPlatformBranding` | — |
| A13 | Multi-language support | MISSING (deliberate) | P2 | Explicit prior decision: English-only, documented in `overnight-prompt-help-menu-2026-07-20.md:501` | Backlog item, not a bug — Albanian branding noted but team opted out of localization for now |
| A14 | Accessibility of public pages (WCAG) | PARTIAL | P2 | No axe-core/jest-axe in CI; some icon buttons below 24px target size | Add automated a11y test to CI; audit icon hit-areas |

### A6 — Waitlist (backlog)
`Waitlist` entity (StudioId, ArtistId?, ClientId/guest contact, preferred date range, Status). `JoinWaitlistCommand` (public, rate-limited) + notify-on-cancellation hook in `CancelAppointmentHandler`. Frontend: "Notify me" CTA on `BookAppointmentForm.tsx` when unavailable, plus a client waitlist-entry list.
**Open questions:** none — fully specified, ready to implement.

### A7 — Gift cards (backlog)
`GiftCard` entity (Code, StudioId, InitialBalance, RemainingBalance, PurchaserEmail, RecipientEmail?, ExpiresAt?) + Stripe PaymentIntent purchase flow; redemption decrements balance against a `Payment`.
**Open questions:** breakage/expiry policy, refund liability handling, whether balances are studio-scoped or platform-wide — needs a product/finance decision before implementation.

---

## Section B — Client

| # | Feature | Verdict | Priority | Evidence | Recommendation |
|---|---|---|---|---|---|
| B1 | Self-service booking, real-time availability | PRESENT | — | `BookAppointmentForm.tsx`, `CheckSlotAvailabilityQuery`, Redis slot-lock | — |
| B2 | Reschedule own appointment | MISSING | P0 | `AppointmentEndpoints.cs:25` reschedule route is `ArtistAndAbove` only; no client command/UI exists | See backlog |
| B3 | Cancel own appointment w/ policy enforcement | MISSING | P0 | Cancel route is also `ArtistAndAbove` only; `AppointmentDetailPage.tsx:230` hides the entire cancel block for clients; no cancellation-window/fee field exists on `DepositRule` | See backlog (depends on D20) |
| B4 | Deposit payment card+cash | PRESENT | — | `CreateDepositPaymentCommand.cs`, `PaymentMethodSelector.tsx` | — |
| B5 | Digital consent+intake forms | PRESENT (differentiator) | — | `frontend/src/features/forms/` | — |
| B6 | Body map + tattoo history | PRESENT (differentiator) | — | `BodyMap.tsx`, `TattooRecord.cs` | — |
| B7 | Design approval workflow | PRESENT (differentiator) | — | `frontend/src/features/designs/` | — |
| B8 | Push/SMS/email reminders, multiple lead times | PRESENT | — | `AppointmentReminderJob.cs` fires at -48h and -24h via email+SMS | — |
| B9 | Add-to-calendar client-side | PRESENT | — | `GetAppointmentIcsQuery.cs`, client-accessible `calendar.ics` route, rendered in `AppointmentDetailPage.tsx:219-228` | Already shipped — no action needed |
| B10 | Package/bundle purchase (prepaid multi-session) | MISSING | P2 | `Design.cs` loosely models a multi-session thread but has no prepaid/session-count/pricing concept | See D9 backlog (owner-side creation covers this) |
| B11 | Membership/loyalty points | N/A / MISSING | P3 | No entity; correctly low priority for infrequent, high-ticket tattoo visits | No action |
| B12 | Referral program client-to-client | MISSING | P1 | Existing `ReferralCode` is studio-to-studio (platform signup) only | See backlog |
| B13 | In-app messaging/two-way chat | MISSING | P1 | Only one-way notifications + a platform bug-report thread (`FeedbackMessage`) exist | See backlog |
| B14 | Tipping at checkout | N/A (out of scope by design) | P3 | App only ever collects the deposit, never full-session payment (`Payment.Amount = appointment.DepositAmount` always) | Revisit if/when full-session in-app payment ships |
| B15 | Saved/preferred payment method | MISSING | P1 | No `StripeCustomerId` on `Client`/`ClientProfile`; every deposit mints a fresh PaymentIntent | See backlog |
| B16 | Multi-studio client view | PRESENT (differentiator) | — | `MyStudiosPage.tsx` | — |
| B17 | Portable tattoo profile | PRESENT (differentiator) | — | `GetPortableProfile` endpoint | — |
| B18 | Client self-service data export/account deletion (GDPR) | MISSING | P2 | No export/delete-account code anywhere | Low-cost hedge given EU-adjacent (Albania) market — see backlog |
| B19 | Mobile app or installable PWA | MISSING | P1 | No manifest/service worker/vite-plugin-pwa | See backlog |
| B20 | Client notification preferences granularity | PRESENT (adequate) | P3 | 9 event types × 2 channels, per-event-and-channel — finer than most competitors | Add Push channel once B19 ships |

### B2 — Client self-reschedule (backlog, P0)
New `RescheduleOwnAppointmentCommand` (ownership check, same pattern as `CreateDepositPaymentCommand`), cutoff window (e.g. no self-reschedule within 24h), reuse existing conflict/slot checks, reschedule reminder jobs, revert `Status` to `Pending` for re-confirmation. New endpoint `PATCH /api/v1/appointments/{id}/self-reschedule`, `ClientAndAbove` + ownership check. Frontend: reschedule affordance on `AppointmentDetailPage.tsx` for non-staff.
**Open questions:** does self-reschedule require artist re-confirmation, or auto-accept? What's the exact cutoff-window default (24h assumed above, needs owner-configurable or platform-default decision)?

### B3 — Client self-cancel (backlog, P0, depends on D20)
New `CancelOwnAppointmentCommand` reusing the refund-logic helper extracted from `CancelAppointmentHandler`. Requires D20's cancellation-window/tiered-refund fields on `DepositRule` to exist first — without them, opening self-cancel to clients today would mean full deposit refunds regardless of notice given.
**Open questions:** none beyond D20's — fully specified once D20 lands.

### B12 — Client-to-client referral (backlog, P1)
`ClientReferralCode` entity (distinct from platform `ReferralCode`): StudioId, ReferrerClientId, Code, RewardType, RedemptionCount. Redemption hook in `CreateAppointmentCommand.cs`.
**Open questions:** reward mechanics (% off next deposit vs fixed amount) — needs a pricing decision.

### B13 — In-app messaging (backlog, P1)
`Conversation`/`ChatMessage` entities, reusing existing SignalR hub infrastructure for live delivery. New `Messaging` Application module + endpoints. Frontend thread view on `AppointmentDetailPage.tsx` + client inbox.
**Open questions:** none — fully specified, ready to implement, though it's a meaningfully sized feature (new domain area, not a quick win).

### B15 — Saved payment method (backlog, P1)
Add `StripeCustomerId` to `ClientProfile.cs` (mirrors existing `Studio.StripeCustomerId` pattern). Extend `IStripePaymentService` for Setup Intents / `off_session` reuse. One-click "pay with saved card" option in `PaymentMethodSelector.tsx`.
**Open questions:** none — fully specified.

### B18 — GDPR export/delete (backlog, P2)
`GET /api/v1/clients/me/export` (JSON dump) + `DELETE /api/v1/clients/me` (soft-delete + PII scrub).
**Open questions:** legal confirmation of which jurisdictions' data-protection law actually applies before treating this as compliance-mandatory rather than a nice-to-have.

### B19 — PWA (backlog, P1)
`vite-plugin-pwa` + manifest + Workbox service worker for offline-shell caching.
**Open questions:** none — fully specified; native app remains explicitly out of scope, PWA is the pragmatic recommendation.

---

## Section C — Artist

| # | Feature | Verdict | Priority | Evidence | Recommendation |
|---|---|---|---|---|---|
| C1 | Personal schedule/calendar view | PRESENT | — | `SchedulePage.tsx` | — |
| C2 | Set working hours + time off | MISSING (frontend only; backend fully wired) | P0 | Zero frontend references to `ArtistSchedule`/`ArtistTimeOff`; `ArtistDetailPage.tsx`'s "Schedule" tab only lists appointments | **Built tonight** — see below |
| C3 | Own portfolio management | PRESENT | — | `ArtistPortfolioPage.tsx` | — |
| C4 | Client intake/consent form review | PRESENT | — | `ConsentFormListPage.tsx` etc. | — |
| C5 | Design/consultation workflow | PRESENT (differentiator) | — | `frontend/src/features/designs/` | — |
| C6 | Commission/session-split visibility for artist | MISSING (effectively) | P1 | All split-related routes/UI are `Owner`-gated; also found a minor authz gap — artist role isn't restricted to own appointments in `GetPaymentByAppointmentQuery.cs` | See backlog |
| C7 | Personal earnings/payout report | MISSING | P0 | Zero hits for earning/payout/commission reporting anywhere | See backlog |
| C8 | Time clock/clock-in-clock-out | N/A | P3 | Confirmed: model is per-session commission/booth-rent, not hourly wages | No action |
| C9 | Booth-rent tracking (flat rent) | MISSING | P1 | `SessionSplit.cs` is a one-off per-payment split, no recurring rent concept, no `ArtistId` FK | See backlog |
| C10 | Own Instagram sync | PRESENT (differentiator) | — | `InstagramTab.tsx` | — |
| C11 | Waitlist management (artist-side) | MISSING | — | No waitlist concept exists at all (see A6) | Covered under A6 |
| C12 | Push notifications (mobile) | CONFIRMED LIMITATION | — | SignalR-only, no service worker/Web Push/native push — real-time degrades to nothing when tab is closed | Ties to B19 |
| C13 | Flash/design catalog | MISSING | P1 | `Design.ClientId` is non-nullable — no concept of a client-less publishable design | See backlog |
| C14 | Supply/inventory tracking | N/A / MISSING | P3 | No entity; not a differentiator even in market research | No action |

### C2 — Artist working-hours/time-off UI (built tonight, see below)

### C6 — Artist earnings visibility (backlog, P1)
Scope `GetPaymentByAppointmentQuery.cs`'s artist-role access to their own appointments (currently unrestricted — a minor authz hardening independent of the UI gap). Add a read-only "My earnings" view reusing the same query.
**Open questions:** none — fully specified.

### C7 — Artist earnings/payout report (backlog, P0)
`GetArtistEarningsSummaryQuery` (sum `SessionSplit.Amount` joined through `Payment`→`Appointment.ArtistId`, grouped by day/week). New endpoint restricting non-owner callers to their own artist id. Frontend: `MyEarningsPage.tsx` reusing the `StatCard` pattern from `DashboardPage.tsx`.
**Open questions:** none — fully specified. (Not built tonight only because it's a new report surface, not a pure UI-affordance-over-existing-data fix like C2 — judged as more than "small.")

### C9 — Booth-rent tracking (backlog, P1)
`Artist.CommissionRate` (nullable decimal) + new `BoothRentSchedule` entity (ArtistId, AmountFixed, Frequency, NextChargeDate, IsActive). Recurring Hangfire job generates rent-charge records; auto-populates a "Booth rent" `SessionSplit` line item.
**Open questions:** none for the tracking mechanism itself; whether rent charges should attempt actual Stripe collection (vs. bookkeeping-only) is a product decision.

### C13 — Flash/design catalog (backlog, P1)
Make `Design.ClientId` nullable, add `IsCatalogItem`/`Price`. `GetDesignCatalogQuery` (public) + `RequestCatalogDesignCommand` (creates an appointment + client-specific copy so booking doesn't mutate the reusable original). Frontend: "Flash" tab on `ArtistPortfolioPage.tsx`, "Book this design" CTA.
**Open questions:** none — fully specified.

---

## Section D — Owner

| # | Feature | Verdict | Priority | Evidence | Recommendation |
|---|---|---|---|---|---|
| D1 | Dashboard KPIs | PRESENT | — | `DashboardPage.tsx` | — |
| D2 | Staff management (add/edit present; schedule/time-off editing NOT present even for owner) | PARTIAL | P0 | Same gap as C2 — no UI reaches the schedule/time-off endpoints for either persona | **Built tonight** (same fix as C2) |
| D3 | Client management CRM | PRESENT | — | `ClientListPage.tsx` | — |
| D4 | Deposit rules engine | PRESENT | — | `frontend/src/features/deposit-rules/` | — |
| D5 | Payments + session splits | PRESENT | — | `frontend/src/features/payments/` | — |
| D6 | Studio profile/branding/SEO | PRESENT | — | `StudioProfilePage.tsx` | — |
| D7 | Subscription/billing management | PRESENT | — | `BillingPage.tsx` | — |
| D8 | Reporting depth (trend, per-artist, busiest day/hour) | MISSING (beyond current-state counts) | P0 | `DashboardPage.tsx` shows only today/week counts + pending deposits — zero revenue figures, zero trend/per-artist/busiest-hour analytics | See backlog |
| D9 | Packages/bundles for sale | MISSING | P1 | No `Package`/`Bundle` entity | See backlog |
| D10 | Gift cards | MISSING | P1 | No `GiftCard` entity | See backlog (touches money/liability — spec only) |
| D11 | Memberships | N/A / MISSING | P3 | Reasonable to deprioritize for infrequent tattoo visits | No action |
| D12 | Marketing email/SMS campaigns | MISSING | P1 | Only transactional notifications exist, no bulk/broadcast capability | See backlog |
| D13 | Promo codes/discounts at booking | MISSING | P1 | Existing `ReferralCode` is platform-signup-only, unrelated to client discounts | See backlog |
| D14 | Multi-location support | MISSING (architectural) | Flag size only | `Plan.MaxLocations` exists but is never enforced — vestigial field; true multi-location is a multi-week tenancy rework | See backlog — explicitly NOT a sprint task |
| D15 | Staff payroll export/accounting integration | MISSING | P1/P2 | No export of any kind exists beyond single-payment PDF invoices | See backlog — phase as CSV export first, OAuth integration later |
| D16 | Retail/POS | N/A / MISSING | P3 | Not a differentiator for this vertical | No action |
| D17 | Business hours/holiday closures (studio-wide) | MISSING | P0 | `Studio.cs` has no operating-hours/closure concept at all; `CheckSlotAvailabilityQuery.cs` only checks per-artist schedules | **Built tonight** — see below |
| D18 | Tax handling (sales tax/VAT) | MISSING | P2 (EU-first market softens urgency) | No tax rate/line anywhere in payment/invoice code | See backlog |
| D19 | Automated no-show fee | PRESENT (via deposit-forfeit) | — | `MarkNoShowCommand.cs` captures/forfeits the deposit — this IS the no-show mechanism | Document as known limitation: no fee if no deposit rule applies |
| D20 | Cancellation policy configuration | MISSING | P0 | `DepositRule.cs` has no cancellation-window/tiered-refund fields; cancel always refunds 100% | See backlog — prerequisite for B3 |
| D21 | Waitlist management (owner-side) | MISSING | — | No waitlist concept exists at all (see A6) | Covered under A6 |
| D22 | Custom booking-form fields | MISSING | P1 | Intake "form" is a single freeform textarea, no structured/configurable fields exist at all | See backlog |
| D23 | Data export (CSV) | MISSING | P1 | Zero export capability beyond single-payment PDF invoice | See backlog |
| D24 | Audit log | MISSING | P1/P2 | `TenantEntity` has no actor-attribution field at all; no `AuditLog` entity | See backlog (same underlying gap as E11 — merge in consolidated backlog) |
| D25 | Onboarding checklist/setup wizard | PRESENT | — | `SetupChecklist.tsx` (2 items today) | Re-add "working hours" step once C2/D2 ship tonight |

### D8 — Revenue/trend reporting (backlog, P0)
`GetRevenueSummaryQuery` (Payment aggregation by day/week/month, per-artist, per-day-of-week/hour). New `ReportEndpoints.cs`, `OwnerOnly`. Frontend "Reports" section with trend chart, per-artist bars, busiest-hour view.
**Open questions:** none — fully specified.

### D9 — Packages/bundles (backlog, P1)
`Package` entity + `PackagePurchase` (SessionsRemaining/ExpiresAt). Optional `PackagePurchaseId` on booking to decrement sessions instead of creating a new payment.
**Open questions:** pricing/expiry policy for unused sessions — product decision needed.

### D10 — Gift cards (backlog, P1) — see A7, same entity serves both surfaces.

### D12 — Marketing campaigns (backlog, P1)
`Campaign` entity + Hangfire fan-out job reusing existing MailKit infra, rate-limited, unsubscribe-compliant. Gate behind a plan flag.
**Open questions:** SMS campaign cost/compliance (opt-in consent record) needs a decision before SMS channel ships; email-only is lower-risk to start.

### D13 — Promo codes (backlog, P1)
`PromoCode` entity, deliberately separate from platform `ReferralCode`. Optional `PromoCodeId` on booking/payment creation.
**Open questions:** none — fully specified.

### D14 — Multi-location (backlog, explicitly NOT a quick fix)
New `Location` entity; `Appointment`/`Artist`/`DepositRule` all need a `LocationId`; tenancy model shifts from `StudioId`-only to `StudioId`+`LocationId` throughout every global query filter.
**Open questions:** billing model for multi-location (one subscription covering N locations vs. per-location billing) — a real pricing decision, plus the scope of the tenancy rework itself needs a dedicated planning pass, not a backlog line.

### D15 — Payroll export (backlog, P1/P2)
Phase 1: CSV export of `SessionSplit` data (low-risk, ships independent of C7/C9 improvements). Phase 2: QuickBooks/Xero OAuth integration.
**Open questions:** which accounting provider to integrate first (Xero has a simpler API; QuickBooks has larger US share) — needs a product decision; Phase 1 (plain CSV) sidesteps this entirely and should ship first regardless.

### D18 — Tax handling (backlog, P2)
Optional `Studio.TaxRatePercent`, applied to deposit/invoice line items.
**Open questions:** which jurisdictions' tax rules actually apply (VAT-inclusive pricing assumption needs legal confirmation) before treating this as more than a display-only field.

### D20 — Cancellation policy configuration (backlog, P0)
`DepositRule.CancellationWindowHours` + `DepositRule.RefundPercentOnLateCancel`. Branch existing refund logic in `CancelAppointmentCommand.cs` on hours-until-appointment.
**Open questions:** none — fully specified. Prerequisite for B3.

### D22 — Custom intake fields (backlog, P1)
`IntakeFormTemplate` entity (JSON field-schema) with owner-side builder; `SubmitIntakeFormPage.tsx` renders the active template dynamically instead of one freeform textarea.
**Open questions:** none — fully specified.

### D23 — CSV data export (backlog, P1)
`GET /api/v1/exports/{entity}?format=csv` (clients/appointments/revenue), streaming CSV, `OwnerOnly`.
**Open questions:** none — fully specified.

---

## Section E — Issuer

| # | Feature | Verdict | Priority | Evidence | Recommendation |
|---|---|---|---|---|---|
| E1 | Platform KPI dashboard | PRESENT | — | `IssuerDashboardPage.tsx` | — |
| E2 | Studio/tenant list, suspend/unsuspend | PRESENT | — | `IssuerStudioListPage.tsx` | — |
| E3 | Studio detail/admin view | PRESENT | — | `IssuerStudioDetailPage.tsx` | — |
| E4 | Plan management CRUD | PRESENT (with a real UX trap) | P2 | The 5 canonical tiers are seeder-owned; an issuer editing them in place gets silently reverted next deploy | See backlog |
| E5 | Subscription oversight | PRESENT | — | `SubscriptionOversightPage.tsx` | — |
| E6 | Referral code management | PRESENT | — | `PlatformReferralPage.tsx` | — |
| E7 | Industry analytics reports | PRESENT | — | `IndustryReportsPage.tsx` | — |
| E8 | Plan usage-limit enforcement | PARTIAL | P1 | Only 2 of 5 dimensions (Artists, AppointmentsPerMonth) are actually enforced; Notifications/Storage/Locations have counters but no gate | See backlog |
| E9 | Dunning/failed-payment recovery | PARTIAL (thin) | P1 | Webhook only flips status; no retry-aware escalating reminders, no grace-period countdown surfaced to owner | See backlog |
| E10 | Support impersonation w/ audit trail | MISSING | P1 | No impersonation code anywhere | See backlog |
| E11 | Audit log of issuer/admin actions | MISSING | P0 | Suspend/unsuspend has zero logging of any kind; other commands have at most an unstructured one-line log | See backlog — merge with D24 |
| E12 | API access/webhooks (`Plan.AllowApiAccess`) | MISSING — sold-but-undelivered | P0 | Flag is a live, help-documented toggle on `PlanEditPage.tsx` with zero backing implementation anywhere | **Built tonight** — see below |
| E13 | Status page/uptime communication | N/A | — | Conventionally a separate hosted service | No action |
| E14 | Manual invoicing beyond cash-activation | PARTIAL | P2 | Cash-activation is the only lever; no refund/credit tool | Low urgency while cash volume is low |
| E15 | Feature flags per plan beyond usage limits | PARTIAL/MISSING | P2 | `PrioritySupport` flag has the same unwired risk as `AllowApiAccess`, lower severity | Same treatment as E12 if/when flagged |
| E16 | Bulk actions on studios | MISSING | P2 | No multi-select anywhere in issuer list pages | See backlog |
| E17 | Churned-studio win-back flow | MISSING | P2/P3 | Only "Reactivate" for admin-suspended studios exists, nothing for voluntary churn | Low-medium priority |
| E18 | Role-based sub-permissions within issuer role | MISSING | P2 | Only 4 flat roles exist platform-wide | Revisit once >1-2 people hold Issuer role |

### E9 — Dunning recovery (backlog, P1)
`PastDueReminderJob` (Hangfire daily, escalating owner notifications at day 1/3/7, reusing existing trial-warning job pattern). Add a `PastDueSince`-derived countdown to the dashboard banner. Add a "days past due" sort column for issuers. Allow a per-subscription dunning-exclusion flag for cash/VIP accounts.
**Open questions:** none — fully specified.

### E10 — Support impersonation (backlog, P1)
`ImpersonationSession` entity (actor, target, reason code, time-boxed expiry, append-only action log). Short-lived JWT with an `imp:true` claim, blocked from destructive/export/billing endpoints by default. Persistent, unmissable "Viewing as {studio}" banner at layout root.
**Open questions:** which specific endpoints should be in the impersonation allow-list (read-only support triage vs. broader access) — needs a product/security decision before implementation; the audit-log dependency (E11) should land first.

### E11 / D24 — Structured admin/audit log (backlog, P0, merged — same underlying gap, different roles affected)
`AuditLogEntry` entity (ActorUserId, ActorRole, Action, TargetType, TargetId, Metadata JSON, CreatedAt). MediatR pipeline behavior (sibling to existing `PlanLimitBehavior`) triggered by a new `IAuditableCommand` marker, applied to Suspend/Unsuspend/ExtendTrial/CancelSubscription/ActivateSubscriptionManually/UpdatePlan/Deactivate-ReactivateReferralCode (issuer side) and Cancel/Delete-client-record/edit-session-splits (owner side, for D24's "who changed what"). New `AuditLogPage.tsx` (issuer) filterable by action/date/target.
**Open questions:** none for the mechanism — fully specified. Retention period for audit entries may need a data-retention policy decision, but that doesn't block building the logging itself.

### E16 — Bulk actions on studios (backlog, P2)
Multi-select column + bulk action bar on `IssuerStudioListPage.tsx`/`SubscriptionOversightPage.tsx`, reusing existing single-row mutations in a loop or a new `BulkExtendTrialCommand(Guid[] StudioIds)`.
**Open questions:** none — fully specified.

---

## Section F — UI/UX heuristics

| # | Heuristic | Verdict | Priority | Evidence | Recommendation |
|---|---|---|---|---|---|
| F1 | Visibility of system status | PRESENT (spot-check only) | — | Skeleton/error states present on newer pages | No new issues |
| F2 | Terminology consistency | PRESENT, minor drift | P3 | "Session Length" used once vs. "Appointment"/"duration" elsewhere | **Built tonight** — trivial rename |
| F3 | User control and freedom | PARTIAL | P2 | No universal Escape-to-cancel on inline-confirm patterns | Add Escape handling to shared inline-confirm component |
| F4 | Consistency and standards | PARTIAL — drift present | P2 | Card-row pattern vs. raw `<table>` coexist in the same `platform` feature folder, shipped 2 days apart | Design-system pass to pick one list primitive |
| F5 | Error prevention | PRESENT, consistent | — | Inline "Confirm?" pattern used consistently for destructive actions | No gap found |
| F6 | Recognition rather than recall | MISSING (uniform baseline) | P3 | No `localStorage` draft-persistence anywhere | Low priority, uniform not regressive |
| F7 | Flexibility/efficiency of use | MISSING | P2 | No bulk actions or keyboard shortcuts anywhere | Same fix as E16, extend to owner-side lists |
| F8 | Aesthetic/minimalist design | PRESENT | — | Newer pages appropriately sparse and well-sectioned | No issues |
| F9 | Error message quality | PARTIAL | P2 | `HelpInsightsPage.tsx` uses generic "Failed to load..." vs. specific copy elsewhere | **Built tonight** — add retry + specific copy |
| F10 | Help/documentation coverage & sync | PARTIAL | P1 | `HelpInsightsPage.tsx` (shipped today, 07-21) has no `helpContent.ts` entry; standalone manual's issuer-plan/studio-detail sections predate the Plan/PlanPrice split and 07-20 referral-codes addition | **Built tonight** — see below |
| F11 | Accessibility WCAG 2.1 AA | PRESENT, minor gaps | P2 | Icon-only buttons generally have visible text nearby; full keyboard-only pass needs a real browser session | Recommend dedicated axe-core/manual follow-up outside this static audit |
| F12 | Mobile responsiveness | PARTIAL | P2 | Issuer action-button rows lack `flex-wrap`, risk clipping at 375px; `HelpInsightsPage.tsx`'s `overflow-x-auto` table pattern isn't replicated elsewhere | **Built tonight** — CSS-only fixes |
| F13 | Dark mode consistency | PARTIAL | P3 | One raw `text-amber-500` icon color lacks a `dark:` variant | **Built tonight** — trivial fix |
| F14 | Timezone handling | MISSING (correctness gap) | P1 | No `Timezone` field anywhere in `Studio`/`Appointment`; no enforced UTC-storage discipline visible at the entity level | See backlog |
| F15 | Empty/loading/error states on pages shipped since 07-04 | PRESENT (mostly), one regression | P2 | `HelpInsightsPage.tsx` models good discipline except for F9's generic error copy | Covered by F9 fix |

### F10 — Help sync (built tonight, see below)

### F14 — Timezone handling (backlog, P1)
Add `Timezone` (IANA string) to `Studio.cs`, defaulted at onboarding. Confirm/enforce UTC storage for all `DateTime` writes (the convention exists in at least one query but isn't demonstrably enforced entity-wide). Convert to studio-local time only at the display/notification layer.
**Open questions:** none for the mechanism — fully specified. Worth prioritizing given guest-artist/tattoo-tourism bookings across timezones are plausible for this vertical.

---

## Consolidated Backlog (deduped, P0 → P3)

### P0
1. **Client self-reschedule** (B2) — new client-scoped command + cutoff window + UI.
2. **Client self-cancel** (B3) — depends on #2 below (D20) landing first.
3. **Cancellation policy configuration** (D20) — `DepositRule` cancellation-window/tiered-refund fields; unblocks B3.
4. **Artist earnings/payout report** (C7) — new reporting surface, self-view of commission.
5. **Owner revenue/trend reporting** (D8) — dashboard is KPI-only today, no revenue/trend/per-artist analytics.
6. **Structured admin/audit log** (E11 + D24, merged) — zero actor-attribution exists anywhere today; a real operational/compliance blind spot.
7. **Sold-but-undelivered `AllowApiAccess` plan flag** (E12) — billing-integrity risk, not just a missing feature. *(Built tonight — mitigated immediately.)*
8. Artist working-hours/time-off UI (C2/D2) and studio-wide closures (D17) were also P0 — *both built tonight*, see below.

### P1
9. Waitlist (A6/C11/D21, merged — one data model, three surfaces: guest "notify me," client waitlist list, artist/owner queue view).
10. Gift cards (A7/D10, merged).
11. Packages/bundles for prepaid multi-session work (B10/D9, merged).
12. Client-to-client referral program (B12).
13. In-app messaging (B13).
14. Saved payment method (B15).
15. Installable PWA (B19).
16. Booth-rent tracking (C9).
17. Flash/design catalog (C13).
18. Marketing email/SMS campaigns (D12).
19. Promo codes/discounts at booking (D13).
20. Custom intake-form fields (D22).
21. CSV data export (D23).
22. Plan usage-limit enforcement completion (E8) — wire the remaining 3 of 5 dimensions.
23. Dunning/failed-payment recovery flow (E9).
24. Support impersonation with audit trail (E10) — should follow #6 (audit log), not precede it.
25. Timezone handling (F14).
26. Booking widget service/style selection (A3).
27. Studio structured hours field (A1) — also feeds A1's public display and D17's closures.
28. Cookie consent banner (A11) — *built tonight*.
29. Sitemap/robots.txt (A10) — *built tonight*.

### P2
30. Multi-location support (D14) — explicitly flagged as a multi-week architectural rework, not a backlog line to schedule casually.
31. Staff payroll export / accounting integration (D15) — phase 1 (CSV) is low-risk, phase 2 (OAuth) needs a provider decision.
32. Tax handling (D18).
33. Bulk actions on studios (E16/F7, merged).
34. Manual invoicing/credit tool beyond cash-activation (E14).
35. Seeder-owned plan tier UX trap (E4).
36. Design-system drift between card-row and table list patterns (F4).
37. Escape-to-cancel on inline-confirm patterns (F3).
38. Per-piece pricing on portfolio images (A2).

### P3
39. Membership/loyalty points (B11/D11) — deliberately low priority for this vertical.
40. Time clock (C8) — confirmed N/A-adjacent, low priority if ever revisited.
41. Supply/inventory tracking (C14/D16).
42. Churned-studio win-back flow (E17).
43. Role-based sub-permissions within issuer role (E18).
44. Multi-language support (A13) — deliberate prior decision, not a regression.
45. GDPR self-service export/delete (B18) — P2/P3 pending legal confirmation of applicable jurisdiction.

---

## What was built tonight (whitelist items only)

- **`docs/claude/_audit-market-notes-2026-07-20.md`** — not created as a separate file; market research was folded directly into this report's summary section by the research subagent, so there was nothing separate to delete.
- **D17 — Studio-wide business hours/holiday closures**: new `StudioClosure` entity + migration, wired into `CheckSlotAvailabilityQuery`, owner-facing CRUD list on the studio profile page, backend + frontend tests. Help: `helpContent.ts` entry + manual section + no tour-step needed (not a primary nav item).
- **C2/D2 — Artist working-hours & time-off editing UI**: RTK Query endpoints for the existing schedule/time-off backend, a weekly-hours editor and time-off list component, wired into the artist's own profile and the owner's artist-detail page. Help: `helpContent.ts` entries for both artist and owner, manual section update, tour step added to both `artistTour.ts` and `ownerTour.ts` pointing at the new UI.
- **A10 — sitemap.xml + robots.txt**: dynamic sitemap endpoint listing active studio/artist slugs; static robots.txt. No user-facing Help entry needed (not a UI surface).
- **A11 — Cookie consent banner**: lightweight banner component on all public routes, localStorage-gated. No Help entry needed (guest-facing, self-explanatory, no settings to document).
- **E12 — Hid the `AllowApiAccess` plan toggle**: removed from `PlanEditPage.tsx` and its `helpContent.ts`/manual mentions until a real API-key/webhook subsystem exists, closing the sold-but-undelivered risk immediately.
- **F10 — Help sync fixes**: added the missing `issuer-help-insights` entry to `helpContent.ts`; updated the standalone manual's `#issuer-studio-detail` (referral codes) and `#issuer-plans` (Plan/PlanPrice split, usage limits, feature flags) sections to match current `helpContent.ts`.
- **F2/F9/F12/F13 — small UI/UX fixes**: "Session Length" → "Appointment Duration" rename; `HelpInsightsPage.tsx` error message made specific with a retry action; `flex-wrap` added to issuer action-button rows; `dark:` variant added to the one raw amber icon color.

*(See commit for the exact diff; each item above shipped with its corresponding test coverage per the project's existing conventions.)*

---

## Round 2 — 2026-07-21 (P0 remediation)

Branch `fix/p0-remediation-2026-07-21`. Builds the six items from the P0 backlog above that
were too large for last night's whitelist pass, now fully scoped and shipped. Full design
rationale lives in `docs/claude/overnight-prompt-p0-remediation-2026-07-21.md`; this section
records what actually shipped, where it diverged from that prompt's citations, and what it
unblocks.

### Shipped tonight (moved from P0 backlog)

1. **Cancellation policy configuration** (D20) — `DepositRule.CancellationWindowHours` /
   `DepositRule.RefundPercentOnLateCancel`, migration `AddCancellationPolicyToDepositRule`,
   `CreateDepositRulePage.tsx`/`DepositRuleDetailPage.tsx` form fields.
2. **Client self-cancel** (B3) — `DELETE /api/v1/appointments/{id}` widened to
   `ClientAndAbove`; role-conditional ownership + refund-percent branch in
   `CancelAppointmentCommand.cs`; new `ClientCancellationPolicy` domain service; cancel
   affordance in `MyBookingsSection.tsx`.
3. **Client self-reschedule** (B2) — `PATCH .../reschedule` widened to `ClientAndAbove`;
   cutoff-gated (no partial-consequence, unlike cancel) using the same notice window;
   reuses `RescheduleDialog.tsx` with a client-facing description override.
4. **Owner revenue & trend reporting** (D8) — `GET /api/v1/reports/revenue-summary`
   (`OwnerOnly`), 12-month trend + per-artist breakdown; new `frontend/src/features/reports/`
   module, `/reports` route + owner nav item.
5. **Structured admin/audit log** (E11 + D24, merged) — new `AuditLogEntry` entity (no
   query filter, `StudioId` nullable), `IAuditableCommand` marker + `AuditLogBehavior`
   pipeline behavior (mirrors `PlanLimitBehavior`), wired onto 9 of the originally-scoped
   commands (see deviations below). `GET /api/v1/platform/audit-log` (issuer, cross-tenant)
   + `GET /api/v1/studios/me/audit-log` (owner, own-studio only).
6. **`Plan.AllowApiAccess` verification** (E12) — re-confirmed clean **except** one real
   regression found: `PlanManagementPage.tsx` still rendered an "API access" badge from
   `plan.allowApiAccess` that last night's fix missed (only the `PlanEditPage.tsx` toggle
   was hidden, not this list-page badge). Fixed tonight. Also found `PrioritySupport`
   (E15) was untouched despite the report flagging the identical risk — it was still a
   live, issuer-editable toggle plus a `PlanManagementPage.tsx` badge plus documented in
   both Help surfaces. Given the same treatment as `AllowApiAccess`.

### Deviations from the prompt's citations (live source won this time)

- **No per-appointment `DepositRuleId` exists.** The prompt assumed an "attached"
  deposit rule per appointment; the actual model is single-active-rule-per-studio
  (`DepositRule.IsActive`, selected by `OrderByDescending(UpdatedAt)` in
  `CreateAppointmentHandler`). Cancellation/reschedule policy checks resolve the
  currently-active rule the same way, rather than an appointment-specific FK.
- **No "delete client record" command exists in this codebase.** Grepped
  `ClientEndpoints.cs` and the whole `Application` layer — there is no delete-client
  command of any name. Not wired into the audit log; nothing to wire.
- **`MrrChart.tsx` does not use recharts** — it's a hand-rolled inline SVG chart; recharts
  isn't installed anywhere in the frontend. The revenue trend chart matches `MrrChart.tsx`'s
  actual hand-rolled-SVG treatment instead, per the "no new npm packages" constraint.
- **Referral-code commands don't carry `StudioId`.** `DeactivateReferralCodeCommand`/
  `ReactivateReferralCodeCommand`/`DeleteReferralCodeCommand` only carry `ReferralCodeId`.
  Rather than adding an async DB lookup to `IAuditableCommand`'s synchronous
  `AuditStudioId` property, these three audit entries currently log as platform-wide
  (`StudioId` null) even though a referral code is conceptually studio-scoped — a known,
  explicitly-accepted limitation, not an oversight.
- **Phase 2's own text briefly implied cancel might have a hard cutoff** ("the window
  still gates whether the client can self-cancel at all"), which contradicts its own
  explicit contrast with Phase 3 ("unlike cancel, reschedule has... a cutoff"). Resolved in
  favor of the explicit contrast: cancel has no cutoff, only tiered refund consequence;
  reschedule has a hard cutoff.

### Newly unblocked

- **E10 — Support impersonation** was explicitly waiting on the audit log (#6 above)
  existing before it could be scoped. That dependency is now satisfied. Still not built —
  it retains its own open product question (which endpoints belong in the impersonation
  allow-list) that this round didn't attempt to resolve.

### Connections worth noting for future work

- **E8 (plan usage-limit enforcement completion)** could plausibly reuse the
  `AuditLogBehavior` pipeline-position precedent once it wires notification/storage/
  location enforcement — both are "cross-cutting MediatR behavior gated by a marker
  interface" shaped problems. Not started; noted for whoever picks up E8.

### Do-not-build-blind list — reconfirmed untouched

Gift cards, packages/memberships, POS/inventory, payroll/commission automation,
multi-location, native mobile, SSO, i18n, tax handling, marketing-campaign sending — none
of these were touched this round, confirmed by diff review against this list.

### Verification

`dotnet build` (0 errors), `dotnet test` (1301 unit + 21+ integration, all green),
`pnpm build`/`tsc -b` (0 TypeScript errors), full frontend `vitest` suite green except one
pre-existing flaky test (`FeedbackInboxPage.test.tsx`, unrelated to this round's changes —
passes in isolation, fails only under full-suite parallel run; not introduced tonight).
