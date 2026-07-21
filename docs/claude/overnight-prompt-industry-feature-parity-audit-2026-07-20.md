# Overnight Master Prompt — Industry Feature-Parity Audit
## (Guest → Client → Artist → Owner → Issuer, Backend + Frontend + UI/UX)

**Date:** 2026-07-20
**Mode:** Fully autonomous. No user present.
**Run with:** `claude --dangerously-skip-permissions`
**Before starting:** `git add -A && git commit -m "chore: pre-feature-audit checkpoint"` then
`git checkout -b audit/industry-feature-parity-2026-07-20`

---

## What this prompt is (read this before anything else)

This is **not** a bug hunt. It is a competitive feature-completeness audit: for every role in the
app, compare what Pena e Artë actually has — in the backend, the frontend, and the UI/UX — against
what is table-stakes or differentiating in the vertical SaaS booking-platform category this product
competes in (salon/spa/wellness booking software, applied to tattoo studios specifically), plus the
general B2B SaaS platform-admin standards that apply to the issuer role (since the issuer is
effectively running a SaaS-of-SaaS: a platform that sells subscriptions to businesses that themselves
run on the platform).

**The deliverable is a gap report with a prioritized backlog, not a fully-built feature set.** Building
a gift-card monetary system, a payroll engine, or a native mobile app overnight without a single
product/business decision behind it (pricing, tax handling, compliance, liability) would be reckless —
this project's own house rule is "consultation and specification, not blind implementation" for
anything beyond a clearly-scoped fix. This prompt draws a hard line (see "What you may actually build
tonight" below) between small, safe, unambiguous wins you should implement directly, and larger gaps
that get a fully-specified backlog entry for a human to prioritize and decide the business rules on.

Do not skip the research grounding step. Do not assume you already know what "industry standard" means
for this category — verify against the current market, not stale training knowledge.

---

## Phase 0 — Ground the audit in the current market (do this first, before opening any code file)

Booking-software feature sets shift. Before auditing anything, search the web for the current state of
the category so your Present/Partial/Missing calls are judged against reality, not assumption. At
minimum, research:

1. Current feature sets of the closest vertical competitors: **Vagaro, Fresha, Boulevard, Mindbody,
   Zenoti, GlossGenius, Booksy, Mangomint, Schedulicity, Square Appointments.**
2. Tattoo-specific competitors if any exist and are documented: **Tattoo Studio Pro, Porter, Linework,
   Venue Ink** (or whatever the current market leaders are — search fresh, don't assume this list is
   still accurate).
3. General B2B SaaS platform-admin standards for multi-tenant subscription businesses (the category the
   **issuer** role operates in): organization/tenant management, plan and seat management, dunning and
   failed-payment recovery, usage metering, audit logging, support impersonation, status pages, API/
   webhook access tiers.
4. Current accessibility and UX baseline expectations for booking/scheduling web apps (WCAG level
   commonly expected, mobile-first patterns, PWA vs native-app expectations for this category in 2026).

Write a short internal summary (a scratch file, e.g. `docs/claude/_audit-market-notes-2026-07-20.md`,
not committed as a permanent doc — delete it at the end or fold its contents into the final report) of
what you found before proceeding. Every "Missing" verdict in the checklists below must be checked
against this research, not against your prior assumptions about what these tools do.

---

## Constraints (apply everywhere, identical to every prior overnight prompt)

- No new npm or NuGet packages **for anything you implement tonight**. If a backlog item genuinely
  requires a new dependency (e.g., a PDF-report library, a payroll-tax library), say so explicitly in
  its backlog entry as a prerequisite decision — do not add it yourself.
- No `useEffect` for data fetching. Approved: resize/keyboard/outside-click/scroll-to listeners,
  clipboard calls, timer side-effects, browser API calls in event handlers, form-state sync from async
  data, geolocation callbacks, analytics-on-mount view tracking, URL/search-param reads on mount.
- TypeScript strict mode. No `any`. No default exports on components. Explicit C# types.
- No business logic in endpoints — MediatR only. Every command has a FluentValidation validator.
- Every DB query on tenant data through EF Core global query filters; `IgnoreQueryFilters()` only where
  already listed in `architecture.md`'s approved-usages table, or newly added AND documented there.
- Every endpoint has `.RequireAuthorization()` with the correct policy, or is a documented
  `AllowAnonymous` exception.
- Never log PII. Structured logs only (Serilog). No secrets in source.
- Anything you build tonight must ship with tests, per the existing convention.

### What you may actually build tonight (whitelist)

Only implement items that are ALL of: (a) clearly-scoped with no open business-rule ambiguity, (b) low
blast-radius (touches one feature area, not core money/auth/tenant logic), (c) reversible, (d) doesn't
require a pricing, tax, legal, or compliance decision. Examples of the kind of thing that qualifies:
adding a missing UI affordance for data that already exists server-side, adding a waitlist *status field*
and basic list UI (not a full waitlist notification engine), adding two-way calendar `.ics` subscription
alongside the existing one-way download, small accessibility fixes, adding an empty/missing settings
toggle that has an obvious default. If in doubt, it goes in the backlog, not in tonight's diff.

**Every whitelist item you build carries a documentation-sync obligation as part of its definition of
done, not as an afterthought:** add or update the corresponding entry in `helpContent.ts`, update the
standalone manual (`frontend/public/user-manual/index.html`) to match, and add/update a
`data-tour="..."` target plus tour step if the change touches a nav item or primary action any of the
four `{client,artist,owner,issuer}Tour.ts` files walk through. A whitelist item that ships without its
Help-content counterpart is not actually done — the in-app Help system existing at all raises the bar
for what "shipped" means, since users can now reasonably expect Help to describe what they're looking at.

### What must go in the backlog instead (do not build blind)

Packages/bundles and their pricing rules, memberships and their billing cadence, gift cards and their
monetary/liability handling, POS/retail inventory, payroll/commission automation beyond the existing
session-split percentages, multi-location support under one owner account, native mobile apps, SSO/SAML,
i18n/localization, an AI receptionist/chatbot, marketing-email campaign sending, and anything that
touches Stripe pricing objects beyond what already exists. For every one of these found missing, write a
complete implementation-ready spec (entities, endpoints, migration shape, frontend components, open
product questions) — same rigor as a real spec handoff — but do not write the code.

---

## Required reading (before auditing anything)

```
CLAUDE.md
docs/claude/architecture.md   — the Feature Module Map (24 features + everything logged since) is your
                                 ground truth for what's ALREADY built. Read the whole file — do not
                                 rely on a stale mental model from a prior session. Cross-reference every
                                 checklist item below against it AND against the live source, since the
                                 map itself can lag behind what later prompts actually shipped or changed
                                 (the Plan/PlanPrice split on 2026-07-19 is the most recent example of a
                                 change large enough to invalidate assumptions from a week earlier).
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/database.md
docs/claude/conventions.md
docs/claude/self-promotion-prompts.md
```

Also skim (don't re-run) the two prior master audit passes if present —
`overnight-prompt-full-app-master-audit-2026-07-20.md` and the individual 2026-07-01 role QA prompts —
so you don't re-report a bug as a "missing feature" when it's actually a known, already-tracked defect.
This prompt is about **what doesn't exist or is incomplete relative to the market**, not about
re-litigating known bugs.

---

## Method — how to audit each item below

For every checklist row across every section:

1. **Locate.** Grep/read the actual backend (`Pena_e_Arte.Domain`, `.Application`, `.Infrastructure`,
   `.API`) and frontend (`frontend/src/features/**`) for the capability. Do not trust the Feature Module
   Map alone — verify in source.
2. **Classify.**
   - `PRESENT` — fully implemented, reachable by the intended role, works end-to-end.
   - `PARTIAL` — backend exists but no frontend (or vice versa), OR it exists but is incomplete/buggy in
     a way that makes it not production-credible, OR it's gated behind a flag that's never surfaced.
   - `MISSING` — does not exist in any form.
   - `N/A` — genuinely does not apply to a tattoo-studio vertical even though it's common in the wider
     category (state why, don't just assume).
3. **Rate priority for gaps** (`PARTIAL`/`MISSING` only):
   - `P0` — table-stakes in this category; a studio evaluating this product against Vagaro/Fresha/etc.
     would consider its absence disqualifying.
   - `P1` — expected by a meaningful subset of studios/artists; absence is a competitive disadvantage but
     not disqualifying.
   - `P2` — nice-to-have, differentiator-tier, not commonly a deciding factor.
   - `P3` — low priority or vertical-specific edge case.
4. **Record.** One row per item in the final report (format specified at the end). For `MISSING`/`PARTIAL`
   items rated `P0`/`P1`, write a short implementation sketch (2–6 lines: entities/endpoints/components
   touched) even if it's going to the backlog, not tonight's diff.

---

# SECTION A — Guest / Public-Facing Surface

Benchmark: the public-facing side of Fresha/Booksy/GlossGenius storefronts, plus general SEO/local-search
expectations for local-service businesses.

| # | Feature | Notes |
|---|---|---|
| A1 | Public studio profile page with photos, hours, location, reviews | Already have `StudioPortfolioPage` — verify hours/opening-times are actually displayed; check if `Studio` has a structured hours/schedule field at all or only artist-level schedules |
| A2 | Public artist portfolio with work samples, specialties, pricing | Have `ArtistPortfolioPage` — check whether flash/pricing per style is shown anywhere publicly |
| A3 | Online booking widget (embeddable + standalone) | Have `EmbedPage` + `/s/{slug}` booking CTA — verify the widget supports picking a specific service/style upfront, not just an artist+duration |
| A4 | Map / "studios near me" discovery | Have — `DiscoverPage`, `StudioMapPage` |
| A5 | Review aggregation + display (Google-style star ratings, review count) | Have — in-house `Review` entity. Check: is there any sync/import from Google Business Profile or Instagram, or is it 100% first-party reviews only? First-party-only is common and fine for this tier — mark PRESENT but note the gap vs. platforms that aggregate external reviews too |
| A6 | Waitlist / "notify me if a slot opens" for a fully-booked artist | Check for any `Waitlist` entity or equivalent. Likely MISSING — this is a standard feature on Fresha/Vagaro/Boulevard for popular providers |
| A7 | Gift cards purchasable by the public | Check for any gift-card entity/endpoint. Likely MISSING |
| A8 | Group/party booking (multiple people booking together) | Less common for tattoo specifically than for salons — check whether any multi-client booking flow exists; likely N/A or low-priority for this vertical, but confirm reasoning |
| A9 | Social proof / trust signals (verified-booking badge on reviews, "X bookings this month") | `IsVerifiedBooking` exists on reviews — confirm it's actually rendered on the public page, not just computed and unused |
| A10 | SEO fundamentals: sitemap.xml, robots.txt, structured data (JSON-LD), canonical tags, meta descriptions | JSON-LD and canonical tags were added in a prior pass for studio/artist pages — verify `sitemap.xml`/`robots.txt` exist at all (check `frontend/public/` or equivalent, and whether they're dynamically generated to include all active studio/artist slugs, or static/missing) |
| A11 | Cookie consent / privacy banner | Check if one exists. Depending on target markets (EU studios) this may be a compliance gap, not just a feature gap — flag accordingly |
| A12 | "Powered by" / platform branding on booking widget | Have — `ShowPlatformBranding` |
| A13 | Multi-language support on public pages | Check — likely English-only. Rate P2/P3 unless the target market is explicitly multilingual (Note: studio name suggests Albanian/Balkan branding — check `CLAUDE.md`/`docs/claude/self-promotion-prompts.md` for any stated target market that would raise this to P1) |
| A14 | Accessibility of public pages (WCAG) | Audit contrast, alt text, keyboard nav, focus order on `DiscoverPage`, `StudioPortfolioPage`, `ArtistPortfolioPage`, `EmbedPage`, booking widget specifically — this is the highest-traffic, highest-conversion-impact surface in the app |

---

# SECTION B — Client-Facing Surface

Benchmark: the customer-facing app experience of Fresha/Vagaro/Boulevard/GlossGenius.

| # | Feature | Notes |
|---|---|---|
| B1 | Self-service booking with real-time availability | Have — `BookAppointmentForm` + slot-check |
| B2 | Reschedule own appointment | **Confirm current state carefully.** A prior audit found the backend `RescheduleAppointmentCommand` is `ArtistAndAbove` only and a client-facing "request new time" flow was explicitly out of scope for the frontend reschedule-UI prompt. From an industry-standard lens this is very likely `MISSING` at `P0` — nearly every competitor lets a client self-reschedule (usually within a cutoff window) without calling the studio. Write a full spec: new command with its own authorization + business rules (does it need artist approval? re-trigger deposit? respect a cutoff window like "no self-reschedule within 24h"?) |
| B3 | Cancel own appointment (self-service, with cancellation-policy enforcement) | Check whether a client can cancel their own booking at all today, and whether there's any cancellation-window/fee policy, or whether cancellation is staff-only. Likely gap — same P0 reasoning as B2 |
| B4 | Deposit payment (card + cash) | Have |
| B5 | Digital consent + intake forms | Have — a genuine differentiator vs. generic salon software, which usually doesn't have tattoo-specific consent workflows built in |
| B6 | Body map + tattoo history | Have — differentiator |
| B7 | Design approval workflow (view/approve/request-changes on artwork) | Have — differentiator; most salon-vertical competitors have nothing like this since it's tattoo-specific |
| B8 | Push/SMS/email reminders before appointment | Check Hangfire jobs for reminder scheduling (48h reminder is referenced in `architecture.md`'s Hangfire conventions example) — confirm it's real and covers multiple lead times (e.g., 48h + 2h), not just one |
| B9 | Add-to-calendar (client side) | Client has no ICS download today per the original QA passes (only artist/owner do via `/appointments/{id}/ics`, `ArtistAndAbove`) — this is a real, easy `P1` gap: clients want to add their tattoo appointment to their personal calendar too. Small, safe, whitelist-eligible fix |
| B10 | Package/bundle purchase (e.g., prepaid multi-session sleeve package) | Check for any entity. Likely `MISSING` — tattoo sleeves/large pieces are commonly billed as multi-session projects; verify whether the app has ANY concept of a multi-session project linking several appointments together, or whether every appointment is fully independent. This is worth flagging even if full "packages" (prepaid bundles) are backlog-only — a lighter "linked session / project" concept might be closer to `PARTIAL` if `DesignRevision`/`Design` already loosely serves this role |
| B11 | Membership / loyalty points program | Check. Likely `MISSING` — lower priority for tattoo (infrequent, high-ticket, not habitual like a haircut) — rate `P2`/`P3` and say why |
| B12 | Referral program (client-to-client, not studio-to-studio) | The existing `ReferralCode` system is studio-to-studio (issuer-level). Check whether there's any CLIENT-facing "refer a friend, both get X" mechanic. Likely `MISSING`, rate `P1` — this is common in the category and drives acquisition differently than the studio-referral system |
| B13 | In-app messaging with the studio/artist (two-way chat) | Check for any messaging/chat feature beyond notifications. Likely `MISSING` — flag as `P1`; many competitors now bundle SMS/DM inboxes into one thread |
| B14 | Tipping at checkout | Check the payment flow for any tip-line item. Likely `MISSING`, rate `P1`/`P2` depending on how deposits vs. final payment are handled currently (verify: does the app ever collect the FULL session payment, or only ever the deposit, with the rest handled in-person? If the latter, tipping-in-app is naturally out of scope — say so) |
| B15 | Saved/preferred payment method (stored card) | Check whether Stripe's saved-payment-method / customer object is used, or whether every deposit requires re-entering card details. Likely gap, rate `P1` |
| B16 | Multi-studio client view ("My Studios") | Have — a genuine differentiator (most competitors don't have true cross-tenant client identity) |
| B17 | Portable tattoo profile (cross-studio history sharing, opt-in) | Have — differentiator |
| B18 | Client self-service data export / account deletion request | Check for GDPR-style self-service. Likely `MISSING` or partial — rate against target market; if EU studios are in scope this could be `P0`/compliance rather than a feature nice-to-have |
| B19 | Mobile app or installable PWA | Check `frontend/` for any PWA manifest/service worker. Likely `MISSING` — nearly every competitor has a native app or at least an installable PWA with push notifications. Rate `P1` given the category norm, note the size of the gap honestly (native app is out of tonight's scope regardless — backlog it with the PWA-first path as the pragmatic recommendation) |
| B20 | Client notification preferences (granular: SMS on/off, email on/off, per-notification-type) | Have some via `notification-preferences` — verify granularity matches competitor norms (per-channel AND per-event-type, not just a global on/off) |

---

# SECTION C — Artist-Facing Surface

Benchmark: the staff/provider app experience of Vagaro/Boulevard/Mangomint (these platforms are
explicitly built around multi-staff scheduling + commission).

| # | Feature | Notes |
|---|---|---|
| C1 | Personal schedule / calendar view | Have — `SchedulePage` |
| C2 | Set working hours + time off | Have (backend); confirm the frontend editing UI (a prior pass explicitly deferred building this — check if it ever got built in a later prompt; if not, this is a real, already-known `P0` gap worth restating here since it blocks a core staff workflow) |
| C3 | Own portfolio management (bio, images, specialties, rate) | Have |
| C4 | Client intake/consent form review | Have (read-only for artist) |
| C5 | Design/consultation workflow | Have — genuine differentiator |
| C6 | Commission / session-split visibility | Have `SessionSplitsEditor` (owner-editable) — check whether the ARTIST can at least *view* their own split/earnings, or whether that's owner-only. If artist can't see their own commission, that's a `P1` gap — staff trust and retention in this category depend on earnings transparency |
| C7 | Personal earnings/payout report (this week/month, by appointment) | Check for any artist-facing earnings summary. Likely `MISSING` — competitors (Vagaro, Boulevard) universally offer providers a "my earnings" view. Rate `P0`/`P1` |
| C8 | Time clock / clock-in-clock-out for hourly or shift-based staff | Check. Likely `N/A`/`P3` for tattoo (artists are typically commission/booth-rent, not hourly) — confirm this assumption against how `SessionSplit` models the business relationship before marking `N/A` |
| C9 | Booth-rent tracking (artist pays studio a fixed rent instead of/alongside commission) | Check if `SessionSplit` or any entity supports a flat booth-rent model vs. percentage-only. Likely `MISSING` — booth rent is extremely common in tattoo specifically (more so than in salons) and is a strong differentiator opportunity if added. Rate `P1`, full spec if missing |
| C10 | Own Instagram sync / social proof management | Have — differentiator |
| C11 | Waitlist management (see own waitlist entries, manually slot someone in) | Depends on A6 — if waitlist is built, artist needs a way to act on it |
| C12 | Push notifications for new bookings/cancellations (mobile) | Ties to B19 — likely `MISSING` at the native/PWA-push level; in-app/SignalR real-time exists, confirm it doesn't degrade to nothing when the artist's tab isn't open (i.e., no true "phone buzzes" experience today) |
| C13 | Flash/design catalog (pre-made designs artists offer, clients can book directly against) | Check. Likely `MISSING` — this is a named feature in tattoo-specific competitors (Venue Ink, Tattoo Studio Pro mention "flash management" explicitly per market research). Real differentiator opportunity, full spec if missing, rate `P1` |
| C14 | Supply/inventory tracking (ink, needles, aftercare product stock) | Likely `MISSING`/`N/A` for now — common in general salon software (retail inventory) but less core for tattoo; rate `P2`/`P3` and justify |

---

# SECTION D — Owner-Facing Surface

Benchmark: the "back office" of Vagaro/Boulevard/Mindbody/Zenoti — this is where the biggest true feature
gaps against the category are likely to live, since these platforms are built to fully replace a
studio's entire back-office stack.

| # | Feature | Notes |
|---|---|---|
| D1 | Dashboard KPIs (bookings, revenue, deposits due) | Have — `DashboardPage` |
| D2 | Staff management (add/edit/remove artists, schedules, time-off) | Have |
| D3 | Client management (CRM) | Have |
| D4 | Deposit rules engine | Have |
| D5 | Payments + session splits | Have |
| D6 | Studio profile / branding / SEO settings | Have |
| D7 | Subscription/billing management (own SaaS plan) | Have |
| D8 | Reporting: revenue over time, by artist, by service type | Check depth — `DashboardPage` shows current-state KPIs; verify whether there's any historical trend reporting (month-over-month revenue, per-artist revenue breakdown, busiest days/hours). Likely `PARTIAL` — basic dashboard exists, deeper BI-style reporting is the gap. Rate `P0`/`P1`, this is core to every competitor's owner value proposition |
| D9 | Packages/bundles for sale (prepaid sessions, sleeve packages) | See B10 — owner-side creation of these. Likely `MISSING`, full spec, `P1` |
| D10 | Gift cards (issue, redeem, track balance) | Likely `MISSING`. Full spec (this touches money — Stripe balance/liability handling needs a real product decision on breakage, refunds, expiry). `P1` |
| D11 | Memberships (recurring client billing for perks/discounts) | Likely `MISSING`/`N/A` for tattoo (infrequent visits) — rate `P2`/`P3`, justify against the vertical |
| D12 | Marketing: email/SMS campaign broadcast to client list | Likely `MISSING` beyond transactional notifications. Full spec — this is commonly a paid add-on tier in competitors (Boulevard, Mangomint), not a given even there, so rate `P1` not `P0` |
| D13 | Promo codes / discounts at booking | Likely `MISSING` outside the studio-referral coupon mechanism. Rate `P1`, full spec |
| D14 | Multi-location support under one owner account | Confirm: today, one `Studio` = one tenant = one owner login. Does an owner with two physical locations have to run two completely separate accounts/subscriptions? Almost certainly yes today. This is a `P0`/`P1` gap for any studio chain — but it is also one of the largest architectural changes possible (tenant model assumes 1 studio = 1 tenant throughout). Full spec only, explicitly flag the architectural size of this one, do not understate it |
| D15 | Staff payroll export / accounting integration (QuickBooks, Xero) | Likely `MISSING`. Rate `P1`/`P2` — full spec, note this typically requires OAuth integration with a named accounting provider, a real product decision on which one(s) to support |
| D16 | Retail/POS for product sales (aftercare products, merch) | Likely `MISSING`/`N/A`. Rate `P2`/`P3` unless studios are known to sell retail heavily — justify |
| D17 | Business hours / holiday closures (studio-wide, distinct from per-artist schedules) | Check whether `Studio` has any studio-level operating-hours or holiday-closure concept independent of individual artist schedules. Likely gap — without it, booking could show a slot on a day the whole studio is closed for a public holiday even if an individual artist's weekly schedule doesn't know about it. Rate `P0`/`P1`, this is a real correctness gap, not just a nice-to-have, and is small enough to be whitelist-eligible if scoped tightly (a `StudioClosure` date-range list checked in `CheckSlotAvailabilityQuery` alongside existing artist-level checks) |
| D18 | Tax handling (sales tax / VAT on deposits, invoices) | Check whether any tax calculation exists anywhere in the payment flow. Likely `MISSING`. This is jurisdiction-dependent and needs a product decision (which tax regime, what data Stripe Tax needs) — full spec, rate by target-market applicability |
| D19 | Automated no-show fee (charge a fee, not just forfeit deposit, on no-show) | Check `MarkNoShowCommand` — does anything happen to the client's payment method beyond the existing deposit-forfeit logic? Likely the deposit-forfeit IS the no-show fee mechanism already — if so this is closer to `PRESENT`/`PARTIAL` than `MISSING`; verify precisely before rating |
| D20 | Cancellation policy configuration (X hours notice required, tiered refund) | Check `DepositRule`/`Studio` for any cancellation-window concept. Ties directly to B3 — likely `MISSING`, `P0`, since it's the backend prerequisite for client self-service cancellation to be safe to ship at all |
| D21 | Waitlist management (owner/studio-wide view) | See A6/C11 |
| D22 | Custom booking-form fields (studio-specific intake questions beyond the built-in intake form) | Check whether `IntakeForm` supports studio-configurable custom fields, or whether the form structure is hardcoded. Likely `MISSING`/`PARTIAL` — rate `P1`, common competitor feature (custom form builder) |
| D23 | Data export (clients, appointments, revenue → CSV) | Check for any export endpoint. Likely `MISSING`, `P1` — this is a trust/no-lock-in feature every serious SaaS in this space offers |
| D24 | Audit log (who changed what, e.g., who cancelled this appointment, who edited this client record) | Check for any per-tenant activity log visible to the owner. Likely `MISSING`, `P1`/`P2` |
| D25 | Onboarding checklist / setup wizard | Have — `SetupChecklist` (though a "working hours" step was previously removed as unfixable cheaply — check if D2/C2's schedule-editing UI, once built, unblocks re-adding a real version of that step) |

---

# SECTION E — Issuer / Platform-Admin Surface

Benchmark: this is not a tattoo-industry question — it's a general B2B SaaS platform-admin question.
The issuer role IS this platform's own back office for running a subscription business. Benchmark
against Stripe Billing's own dashboard, and generic multi-tenant SaaS admin panels (the kind every
Y Combinator SaaS eventually builds): org/tenant management, dunning, usage metering, support tooling,
audit trails.

| # | Feature | Notes |
|---|---|---|
| E1 | Platform KPI dashboard (MRR, churn, trial conversion) | Have — `IssuerDashboardPage` |
| E2 | Studio/tenant list with status filters, suspend/unsuspend | Have |
| E3 | Studio detail / admin view per tenant | Have — `IssuerStudioDetailPage` |
| E4 | Plan management (CRUD tiers, pricing) | Have — post-`PlanPrice`-split |
| E5 | Subscription oversight (extend trial, cancel, manual activation) | Have |
| E6 | Referral code management | Have |
| E7 | Industry analytics reports | Have |
| E8 | Plan usage-limit enforcement + validation report | Have |
| E9 | **Dunning / failed-payment recovery flow** | Check: when a studio's card fails (`past_due`), is there anything beyond a status flag and a banner? Industry-standard dunning includes automatic retry scheduling, escalating email reminders, and a grace-period countdown communicated to the OWNER, not just an issuer-side status column. Likely `PARTIAL` (the state machine exists; the active recovery *campaign* likely doesn't). Rate `P0`/`P1` — this directly protects revenue |
| E10 | **Support impersonation ("log in as this studio" for troubleshooting)** | Check for any issuer capability to view the app as a specific studio would see it, for support purposes, with an audit trail of the impersonation itself. Likely `MISSING`. Rate `P1` — extremely common in this category once a platform has any support burden at all. Full spec (needs careful audit-logging and clear visual indication to avoid this becoming a tenant-isolation violation in disguise) |
| E11 | **Audit log of issuer/admin actions** | Check whether suspend/unsuspend, manual subscription activation, plan edits, referral-code deactivation, etc. are logged anywhere queryable (beyond Serilog request logs). Likely `MISSING` as a structured, queryable admin audit trail. Rate `P0`/`P1` — this is close to a compliance requirement once real money and real businesses are on the platform, not just a nice-to-have |
| E12 | **API access / webhooks for studios** | `Plan.AllowApiAccess` exists as a flag per the Feature Module Map — verify whether there is an ACTUAL public API surface a studio could use, or whether the flag is unwired (check "Plan usage limits" Decisions Log entry — several quota dimensions were flagged as not-yet-wired; confirm whether `AllowApiAccess` is in the same boat). If the flag exists but nothing checks it or exposes a real API, this is `PARTIAL`/effectively `MISSING` and worth flagging as a sold-but-not-delivered feature risk |
| E13 | Status page / uptime communication for studios | Likely `MISSING`/out of scope for an app-level audit (usually a separate hosted service) — mark `N/A` with reasoning unless there's something in-app |
| E14 | Issuer-side impersonated billing / manual invoicing for edge-case studios | Check whether `ActivateSubscriptionManuallyCommand` (cash activation) is the only manual-billing lever, or whether there's a broader manual invoice/credit/refund tool for the issuer. Likely `PARTIAL` |
| E15 | Feature flags per plan beyond usage limits (e.g., "this plan gets early access to X") | Check. Likely `MISSING`/low priority — rate `P2`/`P3` unless a concrete near-term need exists |
| E16 | Bulk actions on studios (e.g., message all trialing studios, bulk trial extension) | Check `IssuerStudioListPage`/`SubscriptionOversightPage` for any multi-select bulk action. Likely `MISSING`, `P2` |
| E17 | Churned-studio win-back flow | Check for anything beyond `Cancelled` status. Likely `MISSING`, `P2`/`P3` |
| E18 | Role-based sub-permissions within the issuer role itself (e.g., a support-only issuer user who can't touch billing) | Check whether `issuer` is a single monolithic role or has any finer-grained internal permission tiers. Likely `MISSING` — flag as `P2` unless the platform team has grown beyond one or two trusted people, in which case it becomes `P1` |

---

# SECTION F — Cross-Cutting UI/UX Heuristic Audit (all roles)

This section is not a feature checklist — it's a usability audit. For each item, spot-check across
representative screens in every role (at minimum: one list page, one detail page, one form, one
dashboard, per role) and record findings the same PRESENT/PARTIAL/MISSING way, with concrete
screen/component references.

| # | Heuristic | What to check |
|---|---|---|
| F1 | Visibility of system status | Loading states, in-flight spinners, real-time updates — largely covered by the prior bug-hunt passes; spot-check it's still true, don't re-audit exhaustively |
| F2 | Match between system and the real world | Do labels/terminology match how tattoo studios actually talk (e.g., "deposit" vs. "booking fee", "session" vs. "appointment") — check for jargon mismatches across roles |
| F3 | User control and freedom | Undo/cancel paths on destructive actions, escape hatches out of multi-step flows (booking form, forms) |
| F4 | Consistency and standards | Do the same UI patterns (buttons, badges, empty states) look/behave identically across `ClientLayout`/`ArtistLayout`/`OwnerLayout`/`IssuerLayout`, or has drift crept in across the many independent feature prompts that built each one separately? |
| F5 | Error prevention | Are there confirmations before destructive/irreversible actions consistently, or does this vary by feature age (older features audited in the 07-01 passes vs. newer ones from 07-04 onward)? |
| F6 | Recognition rather than recall | Are recently-viewed items, recently-booked artists, or draft form state preserved across navigation, or does everything reset? |
| F7 | Flexibility and efficiency of use | Keyboard shortcuts, bulk actions, quick-filters — check whether power users (owners managing a busy studio) have any efficiency affordances beyond one-at-a-time clicking |
| F8 | Aesthetic and minimalist design | Any screens with information overload or, conversely, screens that feel unfinished/sparse relative to their peers |
| F9 | Help users recognize/diagnose/recover from errors | Are error messages specific and actionable (not generic "Something went wrong") across ALL roles — this was fixed piecemeal in prior passes; check for regressions or newer features that skipped this |
| F10 | Help and documentation | **Correction: do not assume this is missing.** As of 2026-07-20/07-21 there is a real in-app Help system: a searchable `HelpMenu` (opened from every layout header, `Shift+?` shortcut), an FAQ accordion, Help Search Analytics (issuer-facing `/platform/help-insights`), a per-role First-Run Onboarding Tour, and a Support Escalation ticket thread — see `architecture.md`'s "In-App Help Menu" / "Help Search Analytics" / "First-Run Onboarding Tour" / "Support Escalation" sections. Audit COMPLETENESS and SYNC instead: (a) does `helpContent.ts` actually cover every screen shipped since, including anything found `MISSING`/`PARTIAL` elsewhere in this audit and later added to the whitelist below; (b) is the standalone manual (`frontend/public/user-manual/index.html`, from `overnight-prompt-user-manual-2026-07-04.md`) still in sync with `helpContent.ts` — the codebase's own docs state these two must be kept in sync and nothing has enforced that since; (c) pull the current `GetHelpSearchInsightsHandler` zero-result/top-query data if reachable — it is literally a live signal of what users can't find help for, so treat it as primary audit input, not just a feature to check the existence of |
| F11 | Accessibility (WCAG 2.1 AA) | Color contrast (especially dark mode), alt text, focus order, aria-labels on icon-only buttons, keyboard-only navigation through the booking flow specifically (it's the money path) |
| F12 | Mobile responsiveness | Every role's primary flows on a 375px viewport — booking form, schedule view, dashboard KPIs, issuer tables are the highest-risk candidates for cramped/broken mobile layouts |
| F13 | Dark mode consistency | Spot-check every role's primary screens in dark mode for contrast/legibility regressions, especially anything added since the original dark-mode implementation |
| F14 | Timezone handling | Does the app handle a studio and its clients being in different timezones at all (e.g., a client booking while traveling), or does it implicitly assume everyone is in the same timezone as the studio? This is a real correctness question, not just UX polish — check how `Date`/`DateTime` values are stored and displayed across booking, schedule, and notification/reminder code |
| F15 | Empty, loading, and error states on every new page shipped since 2026-07-04 | The original QA passes enforced this rigorously on the initial 5-role surface; sample-check whether `MyStudiosPage`, `InstagramTab`, `IssuerStudioDetailPage`, `PlanManagementPage`'s newer sections, and any other page shipped after the original passes actually got the same treatment, or whether the discipline slipped once the original checklist wasn't being followed prompt-to-prompt |

---

## Cross-referencing note

Several rows above deliberately overlap with each other (e.g., D9/B10 packages, D21/C11/A6 waitlist,
D14 multi-location, B19/C12 mobile/push). This is intentional — when you write the final report, merge
these into ONE backlog entry per underlying gap rather than four separate ones, but note every role/
surface each gap touches so the spec covers the full picture (a waitlist feature needs guest-facing
"notify me," client-facing "my waitlist entries," artist/owner-facing "waitlist queue" management, all
from one data model).

---

## Final Deliverable

Write a new file: `docs/claude/industry-feature-parity-report-2026-07-20.md` containing:

```markdown
# Industry Feature-Parity Report — 2026-07-20

## Market research summary
[2-3 paragraphs: what was found researching current competitor feature sets, cited by platform name.
This is the grounding for every verdict below — a future reader should be able to tell WHY something
was rated P0 vs P2.]

## Section A — Guest — full table (item, verdict, priority, one-line evidence, one-line recommendation)
## Section B — Client — same format
## Section C — Artist — same format
## Section D — Owner — same format
## Section E — Issuer — same format
## Section F — UI/UX heuristics — same format, with concrete component references

## Consolidated Backlog (deduped, cross-referenced, prioritized P0 → P3)

For each backlog item:
- Title
- Roles/surfaces affected
- Priority
- Current state (PRESENT/PARTIAL/MISSING, one line)
- Why it matters (one line, tied to the market research)
- Implementation sketch: entities, migrations, endpoints, frontend components (enough for a human or a
  future Claude Code session to start from — this is a spec, written the way this project's engineering
  docs are written elsewhere, e.g. `overnight-prompt-free-plan-tier-2026-07-18.md`'s "Phase 0 — Files to
  read" style)
- Open product/business questions that need a human decision before this can be built (pricing, tax,
  legal, compliance, which third-party integration to pick, etc.) — every backlog item MUST have at
  least a "none — fully specified, ready to implement" line here if genuinely none exist; do not skip
  this field

## What was built tonight (whitelist items only)
- [component/file → what was added → why it qualified for tonight rather than the backlog → the
  corresponding `helpContent.ts` / standalone-manual / tour-step update made alongside it, or an explicit
  note if the change had no user-visible surface and genuinely needed none]

## Deleted scratch file
Confirm `docs/claude/_audit-market-notes-2026-07-20.md` (or wherever Phase 0 notes were kept) was folded
into the Market Research Summary above and removed, not left behind as clutter.
```

Then add a short (5–10 line) pointer entry to `docs/claude/architecture.md`'s Decisions Log or a new
`## Industry Feature-Parity Audit — 2026-07-20` heading, linking to the full report file rather than
duplicating it — architecture.md is already extremely long and this report is a distinct artifact type
(a product backlog, not an architecture decision record).

Commit: `git add -A && git commit -m "audit: industry feature-parity report + safe whitelist implementations"`

---

## Final self-check before declaring done

- Every row in Sections A–F has a verdict. No blank rows, no "TBD."
- Every `P0`/`P1` gap has an implementation sketch.
- Every backlog item has an explicit open-questions field, even if "none."
- Nothing in the "do not build blind" list was built — grep your own diff against that list name-by-name
  before finishing.
- `dotnet build && pnpm build` still clean if anything from the whitelist was implemented; `dotnet test`
  and `pnpm test` green with new tests for anything built.
