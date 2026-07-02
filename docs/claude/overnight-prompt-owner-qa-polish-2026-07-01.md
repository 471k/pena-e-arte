# Overnight Prompt — Owner Role: Autonomous QA → Bug Fix → Polish Loop
**Date:** 2026-07-01
**Mode:** Fully autonomous. No user present. Run until every loop exits clean.

---

## Your Mission

You are the studio owner's first real QA engineer. Your job has two phases.
Do not skip ahead to Phase 2 until Phase 1 exits with a fully green test suite.

**Phase 1 — Bug Hunt:** Walk every owner-accessible screen layer by layer.
Every bug found gets fixed immediately, re-tested, and fixed again if it still fails.
Only move to the next item when the current one is green.

**Phase 2 — Polish:** After all bugs are gone, evaluate every owner-facing screen as
a product manager would before a real launch. Implement each missing piece
systematically until the owner role feels like a complete, professional SaaS product.

---

## Constraints (identical to every other overnight prompt)

- No new npm or NuGet packages.
- No `useEffect` for data fetching. Approved: resize, keyboard, outside-click,
  scroll-to, clipboard calls, timer side-effects (e.g., cooldown countdowns).
- TypeScript strict mode. No `any`. No default exports on components.
- No business logic in endpoints — endpoints call MediatR only.
- Every DB query on tenant data through EF Core global query filters.
  Only `issuer` role may call `IgnoreQueryFilters()`.
- Every endpoint must have `.RequireAuthorization()` with the correct policy.
- Never log PII. All Serilog logs must include `tenant_id`, `user_id`, `request_id`.
- No secrets in source. Environment variables or Vault only.

---

## Required Reading (do before touching any file)

```
CLAUDE.md
docs/claude/backend.md
docs/claude/frontend.md
docs/claude/database.md
docs/claude/architecture.md
docs/claude/conventions.md
```

---

## Owner Surface Map

The owner role lands at `/dashboard` and uses `OwnerLayout`. These are all routes
they can access:

| Route | Component | Purpose |
|---|---|---|
| `/dashboard` | `DashboardPage` | KPIs, today's schedule, cash-pending actions |
| `/schedule` | `SchedulePage` | Week-view calendar, prev/next week navigation |
| `/appointments/:id` | `AppointmentDetailPage` | Single appointment, status transitions |
| `/artists` | `ArtistListPage` | All artists, create new |
| `/artists/new` | `CreateArtistPage` | Create artist form |
| `/artists/:id` | `ArtistDetailPage` | Artist detail, schedule, portfolio, time-off |
| `/clients` | `ClientListPage` | Client list, search |
| `/clients/new` | `CreateClientPage` | Create client form |
| `/clients/:id` | `ClientDetailPage` | Client detail, profile, body map, tattoo history |
| `/clients/:id/tattoos/:tattooId` | `TattooRecordDetailPage` | Individual tattoo record |
| `/designs` | `DesignListPage` | Design approval queue |
| `/designs/new` | `CreateDesignPage` | Start a new design project |
| `/designs/:id` | `DesignDetailPage` | Design detail, revisions, approval, share token |
| `/designs/:id/upload` | `UploadRevisionPage` | Upload a new revision |
| `/payments` | `PaymentListPage` | All payments, cash confirmation |
| `/payments/new` | `CreatePaymentIntentPage` | Manual card payment intent |
| `/payments/:appointmentId` | `PaymentDetailPage` | Payment detail + session splits |
| `/billing` | `BillingPage` | Subscription status, plan management |
| `/billing/subscribe` | `SubscribePage` | Choose and subscribe to a plan |
| `/studios/me` | `StudioProfilePage` | Studio name, city, location, slug, branding, QR, embed, referral |
| `/forms/intake` | `IntakeFormListPage` | All submitted intake forms |
| `/forms/intake/:id` | `IntakeFormDetailPage` | Individual intake form |
| `/forms/consent` | `ConsentFormListPage` | All signed consent forms |
| `/forms/consent/:id` | `ConsentFormDetailPage` | Individual consent form |
| `/deposit-rules` | `DepositRuleListPage` | Studio deposit rules |
| `/deposit-rules/new` | `CreateDepositRulePage` | Create new deposit rule |
| `/deposit-rules/:id` | `DepositRuleDetailPage` | View/edit deposit rule |
| `/notifications` | `NotificationLogListPage` | Notification history |
| `/account/change-password` | `ChangePasswordPage` | Password change |

Backend owner endpoints to verify (all `ArtistAndAbove` or `OwnerOnly` policies):

```
GET    /api/v1/appointments              (ArtistAndAbove)
POST   /api/v1/appointments              (ArtistAndAbove)
GET    /api/v1/appointments/{id}         (ArtistAndAbove)
PATCH  /api/v1/appointments/{id}/confirm (ArtistAndAbove)
PATCH  /api/v1/appointments/{id}/cancel  (ArtistAndAbove)
PATCH  /api/v1/appointments/{id}/complete (ArtistAndAbove)
PATCH  /api/v1/appointments/{id}/no-show  (ArtistAndAbove)
POST   /api/v1/appointments/{id}/reschedule (ArtistAndAbove)

GET    /api/v1/artists                   (ArtistAndAbove)
GET    /api/v1/artists/{id}              (ArtistAndAbove)
POST   /api/v1/artists                   (OwnerOnly)
PUT    /api/v1/artists/{id}              (OwnerOnly)
DELETE /api/v1/artists/{id}              (OwnerOnly)
GET    /api/v1/artists/{id}/schedule     (ArtistAndAbove)
PUT    /api/v1/artists/{id}/schedule     (OwnerOnly)
POST   /api/v1/artists/{id}/time-off     (OwnerOnly)
DELETE /api/v1/artists/{id}/time-off/{timeOffId} (OwnerOnly)

GET    /api/v1/clients                   (ArtistAndAbove)
GET    /api/v1/clients/{id}              (ArtistAndAbove)
POST   /api/v1/clients                   (ArtistAndAbove)
GET    /api/v1/clients/{id}/profile      (ArtistAndAbove)
GET    /api/v1/clients/{id}/tattoos      (ArtistAndAbove)
POST   /api/v1/clients/{id}/tattoos      (ArtistAndAbove)
GET    /api/v1/clients/{id}/tattoos/{tattooId} (ArtistAndAbove)
PUT    /api/v1/clients/{id}/tattoos/{tattooId} (ArtistAndAbove)
DELETE /api/v1/clients/{id}/tattoos/{tattooId} (ArtistAndAbove)

GET    /api/v1/designs                   (ClientAndAbove)
POST   /api/v1/designs                   (ArtistAndAbove)
GET    /api/v1/designs/{id}              (ClientAndAbove)
POST   /api/v1/designs/{id}/revisions    (ArtistAndAbove)
PATCH  /api/v1/designs/{id}/approve      (ClientAndAbove)
PATCH  /api/v1/designs/{id}/request-changes (ClientAndAbove)
POST   /api/v1/designs/{id}/share-token  (ArtistAndAbove)
DELETE /api/v1/designs/{id}/share-token  (ArtistAndAbove)

GET    /api/v1/payments                  (ArtistAndAbove)
POST   /api/v1/payments/intent           (ArtistAndAbove)
GET    /api/v1/payments/{appointmentId}  (ArtistAndAbove)
POST   /api/v1/payments/cash/declare     (ClientAndAbove)
POST   /api/v1/payments/cash/confirm     (ArtistAndAbove)
GET    /api/v1/payments/{appointmentId}/splits (ArtistAndAbove)
PUT    /api/v1/payments/{appointmentId}/splits (OwnerOnly)

GET    /api/v1/billing/subscription      (OwnerOnly)
GET    /api/v1/billing/plans             (OwnerOnly)
POST   /api/v1/billing/checkout          (OwnerOnly)
POST   /api/v1/billing/portal            (OwnerOnly)
POST   /api/v1/billing/finalize-checkout (OwnerOnly)
POST   /api/v1/billing/cancel-plan-change (OwnerOnly)

GET    /api/v1/studios/me                (OwnerOnly)
PUT    /api/v1/studios/me                (OwnerOnly)
PATCH  /api/v1/studios/me/slug           (OwnerOnly)
PATCH  /api/v1/studios/me/branding       (OwnerOnly)
GET    /api/v1/studios/{studioId}/qr     (AllowAnonymous — see AllowAnonymous table)

GET    /api/v1/forms/intake              (ArtistAndAbove)
GET    /api/v1/forms/intake/{id}         (ArtistAndAbove)
POST   /api/v1/forms/intake              (ClientOnly → SubmitIntakeFormPage)

GET    /api/v1/forms/consent             (ArtistAndAbove)
GET    /api/v1/forms/consent/{id}        (ArtistAndAbove)

GET    /api/v1/deposit-rules             (ArtistAndAbove)
GET    /api/v1/deposit-rules/{id}        (ArtistAndAbove)
POST   /api/v1/deposit-rules             (OwnerOnly)
PUT    /api/v1/deposit-rules/{id}        (OwnerOnly)
DELETE /api/v1/deposit-rules/{id}        (OwnerOnly)

GET    /api/v1/notifications             (ArtistAndAbove)
PATCH  /api/v1/notifications/preferences (ArtistAndAbove)

POST   /api/v1/billing/referral-codes    (OwnerOnly — generate referral code)
GET    /api/v1/billing/referral-codes    (OwnerOnly — get own studio's codes)
```

Frontend files in scope:
```
frontend/src/
  layouts/OwnerLayout.tsx
  layouts/__tests__/OwnerLayout.test.tsx
  features/dashboard/
    components/DashboardPage.tsx
    components/SetupChecklist.tsx
    __tests__/DashboardPage.test.tsx
    __tests__/bannerConfig.test.tsx
  features/appointments/
    appointmentsApi.ts
    appointment.types.ts
    components/{SchedulePage, AppointmentDetailPage, AppointmentCard,
      AppointmentStatusBadge, BookAppointmentForm, BookPage,
      DepositStatusBadge, MyBookingsSection}.tsx
    __tests__/{SchedulePage, AppointmentDetailPage, BookPage}.test.tsx
  features/artists/
    artistsApi.ts
    components/{ArtistListPage, CreateArtistPage, ArtistDetailPage, ArtistCard}.tsx
    __tests__/{ArtistListPage, artists}.test.tsx
  features/clients/
    clientsApi.ts
    components/{ClientListPage, CreateClientPage, ClientDetailPage,
      BodyMap, TattooHistorySection, TattooRecordDetailPage,
      MyProfilePage, PortableProfileToggle, ClientCard}.tsx
    __tests__/{ClientListPage, ClientDetailPage, CreateClientPage, BodyMap,
      TattooHistorySection, TattooRecordDetailPage, MyProfilePage,
      PortableProfileToggle, clients}.test.tsx
  features/designs/
    designsApi.ts
    design.types.ts
    components/{DesignListPage, CreateDesignPage, DesignDetailPage,
      DesignCard, UploadRevisionPage, ShareDesignButton}.tsx
    __tests__/{DesignListPage, CreateDesignPage, DesignDetailPage,
      UploadRevisionPage, ShareDesignButton}.test.tsx
  features/payments/
    paymentsApi.ts
    payment.types.ts
    components/{PaymentListPage, PaymentDetailPage, CreatePaymentIntentPage,
      CashDepositConfirmButton, DepositCheckoutPage, PaymentMethodSelector,
      SessionSplitsEditor}.tsx
    __tests__/{PaymentListPage, PaymentDetailPage, CreatePaymentIntentPage,
      CashDepositConfirmButton, DepositCheckoutPage, PaymentMethodSelector,
      SessionSplitsEditor}.test.tsx
  features/billing/
    billingApi.ts
    billing.types.ts
    components/{BillingPage, SubscribePage}.tsx
    __tests__/{BillingPage, SubscribePage}.test.tsx
  features/studios/
    studiosApi.ts
    studio.types.ts
    components/{StudioProfilePage, BrandingSettingsCard, QrCodeSection,
      ReferralCodeCard, EmbedCodeCard}.tsx
    __tests__/{StudioProfilePage, BrandingSettingsCard, QrCodeSection}.test.tsx
  features/forms/
    intakeFormsApi.ts  consentFormsApi.ts  form.types.ts
    components/{IntakeFormListPage, IntakeFormDetailPage,
      ConsentFormListPage, ConsentFormDetailPage,
      SubmitIntakeFormPage, SignConsentFormPage}.tsx
    __tests__/{IntakeForms, ConsentForms}.test.tsx
  features/deposit-rules/
    depositRulesApi.ts (or within studiosApi?)
    components/{DepositRuleListPage, DepositRuleDetailPage, CreateDepositRulePage,
      DepositRuleCard}.tsx
    __tests__/{DepositRuleListPage, DepositRuleDetailPage, CreateDepositRulePage}.test.tsx
  features/notifications/
    notificationsApi.ts
    components/{NotificationBell, NotificationLogListPage,
      NotificationPreferencesCard}.tsx
    __tests__/…
  shared/components/
    ReadOnlyBanner.tsx
    SuspensionBanner.tsx
    SubscriptionGatedButton.tsx
    UserMenu.tsx
    UserChip.tsx

Backend files in scope:
  Pena_e_Arte.Application/Appointments/
  Pena_e_Arte.Application/Artists/
  Pena_e_Arte.Application/Clients/
  Pena_e_Arte.Application/Designs/
  Pena_e_Arte.Application/Payments/
  Pena_e_Arte.Application/Billing/
  Pena_e_Arte.Application/Studios/
  Pena_e_Arte.Application/Forms/
  Pena_e_Arte.Application/DepositRules/
  Pena_e_Arte.Application/Notifications/
  Pena_e_Arte.API/Endpoints/
  tests/Pena_e_Arte.UnitTests/
  tests/Pena_e_Arte.IntegrationTests/
```

---

# PHASE 1 — BUG HUNT

## The Loop Algorithm

```
LOOP:
  1. Build the solution:
       cd "Pena e Arte" && dotnet build
       cd frontend && pnpm build   (catches TypeScript errors)
  2. Run the full test suite:
       dotnet test --no-build
       pnpm test
  3. Collect every failure (build errors + test failures).
  4. For each failure:
       a. Read the relevant source file(s) in full.
       b. Diagnose the root cause precisely.
       c. Fix exactly what is broken — nothing else.
       d. Run just that test file to confirm the fix.
       e. If still failing: diagnose from scratch, fix differently, re-run.
       f. Repeat until green.
  5. After all individual fixes: run the full suite again.
  6. If new failures appeared: back to step 4.
  7. If fully green: EXIT PHASE 1, ENTER PHASE 2.
```

## Audit Checklist — work through while fixing failures

### Layer A — Backend: Authorization + Correctness

Read each endpoint class in `Pena_e_Arte.API/Endpoints/` for the routes listed in the
surface map above. For each endpoint verify:

#### A1. Authorization policies

- Every endpoint has `.RequireAuthorization("PolicyName")` where policy name matches:
  - OwnerOnly endpoints: owner-only operations (delete artist, create deposit rule,
    update session splits, manage studio, branding, billing).
  - ArtistAndAbove: read operations, create appointments, confirm/cancel, cash confirm,
    create client, create design, upload revision.
  - ClientAndAbove: client's own designs, declare cash deposit, deposit checkout.
- No endpoint calls business logic directly — all route to MediatR.
- Every command has a corresponding `FluentValidation` validator registered.
- Every command's validator is registered in DI (check `DependencyInjection.cs` or
  equivalent in Application layer).

#### A2. Appointments

Files: `GetAppointmentsHandler`, `CreateAppointmentHandler`, `ConfirmAppointmentHandler`,
`CancelAppointmentHandler`, `CompleteAppointmentHandler`, `MarkNoShowHandler`,
`RescheduleAppointmentHandler`.

Verify:
- `GetAppointmentsQuery`: accepts `from`, `to` (both optional), `artistId` (optional).
  If `from` and `to` are both null, returns all appointments (needed for owner to see full history).
  If provided, returns appointments where `Date >= from AND Date < to`.
  Always filters by `TenantId` via global query filter.
- `CreateAppointmentCommand`: validates that `artistId` belongs to this tenant (not another studio's artist).
  Validates `date` is in the future. Validates `duration > 0`. Returns 400 on validation failure.
- `ConfirmAppointment`: only transitions `Pending → Confirmed`. Returns 409 if already Confirmed.
- `CancelAppointment`: allowed from Pending or Confirmed. Returns 409 if already Cancelled.
  Fires `SendAppointmentCancellationCommand` as a Hangfire job (not inline await).
- `CompleteAppointment`: only transitions `Confirmed → Completed`. Returns 409 if wrong state.
  Checks `DepositStatus == Paid` before allowing completion (optional: warn, not block).
- `MarkNoShow`: only from `Confirmed`. Sets `Status = NoShow`. Returns 409 if wrong state.
- `RescheduleAppointment`: updates `Date` field, triggers confirmation notification.
  Does NOT reset `Status` back to Pending.
- `GetAppointmentIcsQuery`: returns iCal `.ics` file content for a single appointment.
  Sets `Content-Type: text/calendar`. Verify this endpoint exists and returns correct
  format for adding to calendar apps.

**Common bugs:**
- `artistId` not validated as belonging to the current tenant.
- Appointment date comparison using local time instead of UTC.
- Status transitions not guarded → allows invalid state machine transitions.
- Hangfire not enqueued (notifications called inline, blocking the response).

#### A3. Artists

Files: `GetArtistsHandler`, `GetArtistHandler`, `CreateArtistHandler`,
`UpdateArtistHandler`, `DeleteArtistHandler`, `UpsertArtistScheduleHandler`,
`AddArtistTimeOffHandler`, `DeleteArtistTimeOffHandler`.

Verify:
- `CreateArtist`: requires `firstName`, `lastName`. `email` optional. `slug` auto-generated
  from `DisplayName` if not provided. Slug uniqueness enforced (returns 409 on collision).
- `UpdateArtist`: does NOT regenerate slug. Only updates name, bio, specializations, rate.
- `DeleteArtist`: soft-delete (`DeletedAt = now`). Returns 409 if artist has future appointments.
  Does not delete `PortfolioImages` — they cascade-soft-delete.
- `UpsertArtistSchedule`: accepts a weekly schedule (array of `{ dayOfWeek, startTime, endTime }`).
  Validates `startTime < endTime`. Validates `dayOfWeek` in [0,6]. Upserts — replaces entire
  schedule for the artist.
- `AddArtistTimeOff`: validates `start < end`. Validates dates are in the future.
  Returns 409 if overlaps with existing time-off period for this artist.
- `DeleteArtistTimeOff`: 404 if not found, 403 if time-off belongs to different artist.
- `GetArtistSchedule`: returns weekly schedule + upcoming time-off entries.
  Empty schedule (no rows in DB) returns empty arrays, not 404.

**Common bugs:**
- Delete doesn't check future appointments → can break the schedule page.
- Time-off overlap check missing → allows double-booking of blocked time.
- Slug collision: only checks `IsDeleted = false` records — soft-deleted slugs must also be
  excluded from collision check (otherwise a reactivated artist could have a collision).

#### A4. Clients

Files: `GetClientsHandler`, `GetClientHandler`, `CreateClientHandler`,
`GetClientProfileHandler`, `UpsertClientProfileHandler`,
`AddTattooRecordHandler`, `UpdateTattooRecordHandler`, `DeleteTattooRecordHandler`,
`UpdateBodyMapHandler`, `GetTattooRecordsHandler`, `GetTattooRecordHandler`.

Verify:
- `CreateClient`: requires `firstName`, `lastName`. Optional: `email`, `phone`.
  If `email` is provided, checks uniqueness within tenant (no duplicate clients).
  Returns 409 on duplicate email.
- `GetClients`: accepts optional `search` param (searches `firstName`, `lastName`, `email`).
  Sorting: alphabetical by `lastName, firstName` by default.
- `GetClientProfile`: returns merged data from `Client` + `ClientProfile`. If profile
  doesn't exist yet, returns nulls for profile fields (not 404).
- `UpsertClientProfile`: creates or updates. Validates `allergies`, `medicalNotes` are
  not longer than 2000 chars each.
- `AddTattooRecord`: validates `placement` and `style` are not empty. `imageUrls` is
  an array of R2-signed URLs uploaded via `FileUploadField`. Max 10 images.
- `DeleteTattooRecord`: soft-delete. Returns 404 if not found.
- `UpdateBodyMap`: accepts a JSON body map object (placement identifiers). No validation
  needed beyond it being valid JSON — the body map is a free-form structure.

**Common bugs:**
- `GetClients` search is case-sensitive (`LIKE 'X%'` vs `LIKE '%X%'`) — should be
  case-insensitive, which MySQL handles with collation. Verify `COLLATE utf8mb4_0900_ai_ci`
  on the name columns (accent+case insensitive).
- `AddTattooRecord` doesn't validate the image URLs against R2 pattern — anyone can inject
  arbitrary URLs. Add a simple `Uri.TryCreate` check. Don't block on domain — R2 signed
  URLs can come from multiple domains in dev vs prod.

#### A5. Designs

Files: `CreateDesignHandler`, `GetDesignHandler`, `GetDesignsHandler`,
`UploadRevisionHandler`, `ApproveDesignHandler`, `RequestDesignChangesHandler`,
`CreateDesignShareTokenHandler`, `RevokeDesignShareTokenHandler`.

Verify:
- `CreateDesign`: accepts `clientId`, `title`, `notes`. Validates `clientId` belongs to tenant.
  Creates design in `Draft` status with no revisions.
- `GetDesigns`: returns list filtered by status (optional `status` query param). If status
  not provided, returns all. Always tenant-filtered.
- `UploadRevision`: accepts `imageUrl` (R2 URL after upload). Validates design exists and
  belongs to tenant. Sets `LatestRevisionUrl` on the design entity. Appends to revision history.
  Status transitions: `Draft → InReview` on first revision upload.
- `ApproveDesign`: only from `InReview`. Transitions to `Approved`. Returns 409 on wrong state.
- `RequestDesignChanges`: from `InReview`. Transitions back to `Draft` (client wants changes,
  artist uploads another revision). Returns 409 on wrong state.
- `CreateDesignShareToken`: creates `DesignShareToken` entity. Only one active token per
  design at a time — check for existing active (non-expired, non-revoked) token and return
  it if still valid, or create a new one.
- `RevokeDesignShareToken`: sets `IsRevoked = true` on active token. Returns 404 if no
  active token exists.

**Common bugs:**
- `UploadRevision` doesn't transition status → design stays `Draft` forever.
- Share token creation allows duplicate active tokens → two tokens valid at once.
- `ApproveDesign` doesn't check that the requester is the client who owns the design
  (should be `ClientAndAbove` but must be the RIGHT client).

#### A6. Payments

Files: `GetPaymentsHandler`, `GetPaymentHandler`, `CreatePaymentIntentHandler`,
`DeclareCashDepositHandler`, `ConfirmCashDepositHandler`,
`GetSessionSplitsHandler`, `UpdateSessionSplitsHandler`.

Verify:
- `GetPayments`: returns all payments for this tenant. Optional filter: `status`, `appointmentId`.
  Pagination supported: `pageSize`, `page` (or `cursor`).
- `CreatePaymentIntent`: creates a Stripe PaymentIntent. Must use the platform's own Stripe
  account (no `connectedAccountId` — see architecture). Returns `clientSecret` for the
  Stripe Payment Element.
- `DeclareCashDeposit`: creates `Payment` with `Method = Cash, Status = CashPending`.
  Associates with `appointmentId`. Validates appointment belongs to tenant.
  Only one pending cash payment per appointment at a time.
- `ConfirmCashDeposit`: sets `Status = Paid`. Also updates `Appointment.DepositStatus = Paid`.
  Validates the payment exists and is `CashPending`. Returns 409 if already confirmed.
- `GetSessionSplits`: returns the splits for a specific appointment's payment.
  If no splits exist yet, returns an empty array (not 404).
- `UpdateSessionSplits`: replaces the splits. Validates that percentages sum to 100.
  `OwnerOnly` — artists cannot change their own split.

**Common bugs:**
- `ConfirmCashDeposit` doesn't update `Appointment.DepositStatus` → dashboard still shows
  the payment as pending after confirmation.
- `CreatePaymentIntent` accidentally passes `StripeAccount` to Stripe → rejected by Stripe
  because the platform has no Connect setup. Confirm the service never uses `RequestOptions`
  with a connected account ID.
- `DeclareCashDeposit` allows multiple pending cash payments for the same appointment.

#### A7. Billing

Files: `GetSubscriptionHandler`, `GetPlansHandler`, `CreateCheckoutSessionHandler`,
`CreatePortalSessionHandler`, `FinalizeCheckoutHandler`, `CancelPlanChangeHandler`,
`HandleStripeWebhookHandler` (billing webhook).

Verify:
- `GetSubscription`: returns the studio's current subscription + trial dates + current
  period end. If no subscription exists yet (new studio in trial), returns a synthetic
  `Trialing` response with `trialExpiresAt = studio.TrialExpiresAt`.
- `GetPlans`: returns all available plans (ordered by price ascending). Issuer-defined.
- `CreateCheckoutSession`: creates a Stripe Checkout session for subscription.
  Success URL: `{VITE_APP_URL}/billing?session_id={CHECKOUT_SESSION_ID}`.
  Cancel URL: `{VITE_APP_URL}/billing`.
  Mode: `subscription`.
- `FinalizeCheckout`: called after Stripe Checkout redirect. Verifies session ID via
  Stripe API. Marks subscription as Active. Handles case where webhook already processed
  it (idempotent via `StripeSubscriptionId` check).
- `CancelPlanChange`: removes the pending plan change scheduled for end of period.
  Only works if there's an active `pendingPlanId`. Returns 409 if no pending change.
- `HandleStripeWebhookBilling`: validates `Stripe-Signature`. Processes:
  - `invoice.payment_succeeded` → sets subscription `Active`, updates `CurrentPeriodEnd`
  - `invoice.payment_failed`    → sets subscription `PastDue`
  - `customer.subscription.deleted` → sets subscription `Cancelled`
  - `customer.subscription.updated` → updates plan, period, status
  Logs all events (no PII). Does NOT rethrow Stripe exceptions (returns 200 to Stripe
  even if our handling fails — Stripe will retry if we return non-200).

**Common bugs:**
- Webhook handler returns 400 on signature failure vs 400 on processing error — both
  must return 400 on signature failure (reject), 200 on processing error (accept + log).
- `FinalizeCheckout` not idempotent → calling twice creates duplicate subscription rows.
- `GetSubscription` returns 404 for a studio in trial that has no `Subscription` row yet
  → `BillingPage` crashes because it gets no data.

#### A8. Studio Settings

Files: `GetMyStudioHandler`, `UpdateMyStudioHandler`, `UpdateStudioSlugHandler`,
`UpdateStudioBrandingHandler`.

Verify:
- `GetMyStudio`: returns full studio data including `slug`, `showPlatformBranding`,
  `latitude`, `longitude`, `city`, `instagramHandle`, `phoneNumber`.
- `UpdateMyStudio`: validates `name` not empty, `city` not empty, `latitude` and
  `longitude` in valid ranges. `instagramHandle` optional, strips leading `@` if provided.
- `UpdateStudioSlug`: validates slug format (lowercase, hyphens, max 60 chars, no spaces).
  Checks uniqueness across all tenants (slug is globally unique). Returns 409 on conflict.
  Slug can only be changed once — check if `SlugLockedAt` is set and return 409 if so.
  On success, set `SlugLockedAt = now`.
- `UpdateStudioBranding`: validates that `Plan.AllowBrandingRemoval == true` before
  setting `ShowPlatformBranding = false`. Returns 403 if plan doesn't allow removal.

**Common bugs:**
- `UpdateStudioSlug` doesn't enforce one-time edit → owners can change slug repeatedly,
  breaking all external links.
- `UpdateStudioBranding` doesn't check plan permission → free-tier studios can remove branding.

#### A9. Forms

Files: `GetIntakeFormsHandler`, `GetIntakeFormHandler`, `SubmitIntakeFormHandler`,
`GetConsentFormsHandler`, `GetConsentFormHandler`, `SignConsentFormHandler`.

Verify:
- `GetIntakeForms`: returns intake forms for this tenant, newest first.
- `SubmitIntakeForm`: `ClientOnly`. Validates required fields. Stores submission timestamp.
  Triggers a notification to the owner (Hangfire job).
- `GetConsentForms`: returns consent forms for this tenant.
- `SignConsentForm`: `ClientOnly`. Validates appointment exists and belongs to the signing client.
  Creates signed record. Triggers notification to owner.

#### A10. Deposit Rules

Files: `GetDepositRulesHandler`, `GetDepositRuleHandler`, `CreateDepositRuleHandler`,
`UpdateDepositRuleHandler`, `DeleteDepositRuleHandler`.

Verify:
- `CreateDepositRule`: validates `name` not empty, `type` is `Percentage` or `FixedAmount`,
  `value > 0`, if `Percentage` then `value <= 100`. Returns 400 on invalid.
- `UpdateDepositRule`: same validations as create.
- `DeleteDepositRule`: `OwnerOnly`. Returns 409 if the rule is currently used by any
  active appointment (or soft-delete if entity supports it).
- Rules are tenant-scoped via global query filter — never bleed between studios.

---

### Layer B — Frontend State

#### B1. API slice tag correctness

For each RTK Query slice, verify invalidation tags are correct:

```
appointmentsApi:
  createAppointment  → invalidates ["Appointment"]
  confirm/cancel/complete/noShow/reschedule → invalidates ["Appointment", { id }]

artistsApi:
  createArtist       → invalidates ["Artist"]
  updateArtist       → invalidates ["Artist", { id }]
  deleteArtist       → invalidates ["Artist"]
  upsertSchedule     → invalidates ["ArtistSchedule", { id }]
  addTimeOff         → invalidates ["ArtistSchedule", { id }]
  deleteTimeOff      → invalidates ["ArtistSchedule", { id }]

clientsApi:
  createClient       → invalidates ["Client"]
  upsertProfile      → invalidates ["ClientProfile", { id }]
  addTattooRecord    → invalidates ["TattooRecord"]
  updateTattooRecord → invalidates ["TattooRecord", { id }]
  deleteTattooRecord → invalidates ["TattooRecord"]
  updateBodyMap      → invalidates ["ClientProfile", { id }]

designsApi:
  createDesign       → invalidates ["Design"]
  uploadRevision     → invalidates ["Design", { id }]
  approve            → invalidates ["Design", { id }]
  requestChanges     → invalidates ["Design", { id }]
  createShareToken   → invalidates ["Design", { id }]
  revokeShareToken   → invalidates ["Design", { id }]

paymentsApi:
  confirmCashDeposit → invalidates ["Payment", "Appointment"] (updates appt deposit status)
  updateSessionSplits → invalidates ["SessionSplit"]

billingApi:
  finalizeCheckout   → invalidates ["Subscription"]
  cancelPlanChange   → invalidates ["Subscription"]
```

If any tag is missing, the UI will show stale data after a mutation without a page reload.

#### B2. ReadOnlyBanner + SuspensionBanner + SubscriptionGatedButton

These three shared components are critical to the owner's access-control UX. Read and verify:

**ReadOnlyBanner** (`shared/components/ReadOnlyBanner.tsx`):
- Reads `subscription.status` from RTK Query (via `useGetSubscriptionQuery`).
- Shows a sticky amber banner when `status === "GracePeriod"`.
- Banner is `role="alert"` with a "Subscribe now" link to `/billing/subscribe`.
- Does NOT show for `Active` or `Trialing`.

**SuspensionBanner** (`shared/components/SuspensionBanner.tsx`):
- Reads `studio.isActive` prop.
- Shows a red banner when `!studio.isActive`.
- Contains a mailto link to support.
- Banner is `role="alert"`.

**SubscriptionGatedButton** (`shared/components/SubscriptionGatedButton.tsx`):
- Wraps any action button.
- If `subscription.status === "GracePeriod"` or `"Cancelled"` or `"Suspended"`:
  disables the button and shows a tooltip explaining why.
- If `subscription.status === "Active"` or `"Trialing"`: renders normally.
- Must NOT block all actions in trial — trial is full-access.

Common bugs:
- `SubscriptionGatedButton` blocks trial users (reads `status !== "Active"` instead of
  checking for ONLY the blocked states).
- `ReadOnlyBanner` fires a subscription query even when the user is not an owner (check role
  before calling the query — use `skip: role !== "owner"` in the query options).

---

### Layer C — Frontend Components

Work through each component group in order. For each: read the file, identify bugs,
fix them, ensure the corresponding test covers the fix.

#### C1. OwnerLayout

Verify:
- 8 nav items all present with correct hrefs (Dashboard, Schedule, Artists, Clients,
  Designs, Payments, Billing, Studio Settings → `/studios/me`).
- Active nav item has `bg-primary text-primary-foreground`.
- `NotificationBell` renders in the header.
- `UserMenu` renders in the header with logout handler.
- `useSignalR(tenantId)` is called — real-time events connected.
- `useGetSubscriptionQuery()` called to prime the subscription cache (so child forms
  don't show a flash of unguarded state on first render).
- `useGetMyStudioQuery()` called to prime the studio cache for `SuspensionBanner`.
- Mobile nav: if 8 items overflow on narrow screens, the nav should scroll horizontally
  (`overflow-x-auto scrollbar-none`). Verify this class is present or add it.

#### C2. DashboardPage

Verify:
- `SubscriptionBanner` shows correctly for all statuses (covered by `bannerConfig.test.tsx`).
- `SetupChecklist` renders (and hides itself when all steps are complete — read its logic).
- 3 KPI stat cards: "Today", "This Week", "Deposits Due" — all have loading skeletons.
- `TodaySection` shows appointments for today only (start of day to start of tomorrow).
- `TodaySection` empty state shows two CTA buttons: "Book Appointment" + "View this week →".
- `TodaySection` error state shows a `role="alert"` error message.
- `CashPendingSection` only renders when there are `CashPending` payments.
- "Book Appointment" button in the header navigates to `/appointments/new`.
  **BUT `/appointments/new` does NOT exist as a route.** The correct route for booking
  is `/schedule` (where the owner creates appointments through `BookAppointmentForm`).
  Change the `+ Book Appointment` button to navigate to `/schedule` instead, or ensure
  the route exists. Check the router and fix accordingly.
- `DashboardPage` has a second sticky `<header>` inside `<main>` which is wrong —
  the `OwnerLayout` already provides the app header. The inner header creates a double
  header. Evaluate: the inner header should be removed or changed to a plain `<div>`.
- Date is formatted with `en-GB` locale (`formatDate`) — but `formatTime` uses the
  default locale (`toLocaleTimeString([])`). Standardize to `en-GB` for consistency.

#### C3. SchedulePage

Verify:
- Week navigation: "← Prev" and "Next →" buttons change `weekStart` by 7 days.
- "Today" button returns to the current week (if one exists).
- Week label displays `Mon dd MMM yyyy – Sun dd MMM yyyy` range.
- Loading state shows the `SchedulePageSkeleton`.
- Error state shows `role="alert"` via `useSuspensionAwareError`.
- Empty week shows a "No appointments this week" message with a "Book appointment" action.
- Appointments are grouped by day — each day with at least one appointment shows a day header.
- Days with no appointments are hidden (not shown as empty rows).
- `AppointmentCard` is clickable and navigates to `/appointments/:id`.
- Today's date is highlighted in the day headers (e.g., `font-bold` or ring).

#### C4. AppointmentDetailPage

Verify:
- Shows: date/time, artist name (looked up from artists list or API), client name, status,
  deposit status, notes.
- Status badge uses `AppointmentStatusBadge`.
- Action buttons rendered based on current status:
  - `Pending` → "Confirm" + "Cancel"
  - `Confirmed` → "Complete" + "Mark No-Show" + "Reschedule" + "Cancel"
  - `Completed` → no actions (read-only)
  - `Cancelled` → no actions (read-only)
  - `NoShow` → no actions (read-only)
- Each action button is `disabled` and shows a spinner while the mutation is in-flight.
- Each destructive action (Cancel, Mark No-Show) shows an inline confirmation step.
- After a status change: the status badge updates without a page reload (RTK Query
  invalidation handles this).
- "Add to calendar" button: calls `GET /api/v1/appointments/{id}/ics` and triggers a
  download. Verify this feature exists end-to-end. If the backend endpoint is missing,
  implement it.
- Payment section: shows deposit status. If `CashPending`, shows `CashDepositConfirmButton`.
  If no payment: shows "No payment on record".

#### C5. ArtistListPage + CreateArtistPage + ArtistDetailPage

**ArtistListPage:**
- Shows each artist's name, initials avatar (or profile image), specialty, working schedule
  indicator.
- "Add Artist" button → `/artists/new` (OwnerOnly — guard in component if needed).
- Loading skeleton while query is in flight.
- Empty state: "No artists yet. Add your first artist to start taking bookings."
- Search by name.

**CreateArtistPage:**
- Form fields: firstName, lastName, email (optional), specializations (optional, multi-value),
  hourlyRate (optional number).
- Validates firstName and lastName are not empty.
- On success: navigates to `/artists/:id` and shows a success toast.
- "Cancel" navigates back to `/artists`.

**ArtistDetailPage:**
- Shows: full name, specialty, rate, bio, profile image (or initials avatar), Instagram.
- Schedule section: shows weekly working hours. "Edit schedule" button opens schedule form.
- Portfolio section: shows artist's portfolio images. Images link to lightbox.
- Time-off section: upcoming time-off entries. "Add time off" form.
- Edit artist button (OwnerOnly) → edit inline or navigate to edit page.
- Delete artist button (OwnerOnly) → confirm step → calls delete mutation.
  Disabled if artist has future appointments (backend returns 409, show the message).

#### C6. ClientListPage + CreateClientPage + ClientDetailPage

**ClientListPage:**
- Search by name or email.
- Each row shows: name, email (or "No email"), last appointment date.
- "Add Client" button → `/clients/new`.
- Empty state + no-match state.
- Sort by name (default) — allow toggling sort by last appointment date.

**CreateClientPage:**
- Fields: firstName, lastName, email (optional), phone (optional).
- Validates firstName, lastName required.
- On success: navigates to `/clients/:id`.

**ClientDetailPage:**
- Header: client name, email, phone, "Edit" (inline or separate).
- Tabs or sections:
  - **Profile**: allergies, medical notes, skin type. "Edit profile" form with `UpsertClientProfile`.
  - **Body Map**: `BodyMap` component. Shows a body outline SVG with placed tattoo markers.
    "Edit" toggles placement mode. Saves with `UpdateBodyMap` mutation.
  - **Tattoo History**: `TattooHistorySection`. List of tattoo records. Each shows placement,
    style, date, images. "Add tattoo record" form. Each row links to `TattooRecordDetailPage`.
  - **Portable Profile**: `PortableProfileToggle`. Shows current opt-in status. Toggle saves
    with `UpdatePortableProfileOptIn` mutation.
- Appointments section (optional): list of this client's appointments at this studio.

**BodyMap:**
- SVG-based body outline with clickable placement zones.
- Active placements shown in distinct color (violet).
- `aria-label` on the interactive SVG and on each zone button.
- Touch targets on each zone meet 44px minimum.

**TattooHistorySection:**
- Each record: placement, style, images (thumbnail grid), date.
- "Add record" opens an inline form or dialog.
- Image upload uses `FileUploadField` → R2.
- Delete record shows confirm step.

#### C7. DesignListPage + CreateDesignPage + DesignDetailPage

**DesignListPage:**
- Filter by status: All | Draft | InReview | Approved | ChangesRequested.
- Each card shows: client name, title, status badge, last revision thumbnail.
- "New Design" button → `/designs/new` (ArtistAndAbove).
- Empty state (no designs) + no-match state (filter returns empty).
- Status badge colour: Draft=muted, InReview=blue, Approved=green, ChangesRequested=amber.

**CreateDesignPage:**
- Fields: client selector (dropdown from `useGetClientsQuery`), title, notes.
- "Cancel" navigates back.
- On success: navigates to `/designs/:id/upload` to immediately upload the first revision.

**DesignDetailPage:**
- Header: title, client name, status badge.
- Revision history: list of revision images in reverse-chronological order. Each shows
  the R2 image, upload date.
- Current revision: large preview of `latestRevisionUrl`.
- Actions (based on status):
  - `Draft` → "Upload revision" (link to `/designs/:id/upload`)
  - `InReview` → "Approve" + "Request changes" (for client role; artist sees waiting state)
  - `Approved` → "Completed" badge, no actions
  - `ChangesRequested` → "Upload revision" (artist's turn)
- Share token section:
  - "Generate share link" button → calls `createShareToken` mutation.
  - Shows the generated link with a copy button.
  - "Revoke link" button → confirm step → calls `revokeShareToken` mutation.
  - Link format: `{VITE_PUBLIC_URL}/share/{token}` (NOT `window.location.origin` — see
    EmbedPage note in architecture.md about public URL env var).

**UploadRevisionPage:**
- `FileUploadField` for the image.
- "Upload" disabled until a file is selected.
- Shows upload progress.
- On success: navigates to `/designs/:id`.
- On error: shows descriptive error (R2 upload failed, file too large, etc.).

#### C8. PaymentListPage + PaymentDetailPage + CreatePaymentIntentPage

**PaymentListPage:**
- Columns: client name, amount, method (Card/Cash), status badge, appointment date.
- Filter by status: All | CashPending | Paid | Failed.
- `CashDepositConfirmButton` inline for CashPending rows.
- "New Payment" button → `/payments/new`.
- Empty state.
- Pagination: load-more or page navigation for large sets.

**PaymentDetailPage:**
- Shows: amount, method, status, appointment link, created date.
- Session splits section: pie chart or table of artist/owner splits.
- "Edit splits" form (OwnerOnly): percentage inputs that must sum to 100.
  `SessionSplitsEditor` component.
- "Confirm cash payment" button for `CashPending`.

**CreatePaymentIntentPage:**
- `PaymentMethodSelector`: card vs cash selector.
- If card: shows Stripe Payment Element. Amount field. Client selector.
  On submit: calls `CreatePaymentIntent` → renders `StripePaymentElement`.
- If cash: redirects to `DeclareCashDeposit` flow.
- On success: shows confirmation, navigates to appointment.

**CashDepositConfirmButton:**
- Shows client name and amount.
- Confirm step: "Mark €X from {clientName} as received?"
- Calls `ConfirmCashDeposit` on confirm.
- Shows spinner while in-flight, disabled during confirmation.
- On success: toast "Cash payment of €X confirmed."

#### C9. BillingPage + SubscribePage

**BillingPage:** (mostly read-only reference — already quite complete)
- Verify `useEffect` for `session_id` finalization is correct and not double-firing
  (protected by `finalizedRef.current`). ✓
- Verify `BillingPageSkeleton` shows during both `loadingSub` and `loadingPlans`. ✓
- Verify suspended studio card shows correctly when `!studio.isActive`. ✓
- Cash-billed active subscription shows "Switch to card billing" option. ✓

**SubscribePage:**
- Shows plan cards: name, price per month, features, yearly price (if available).
- "Yearly" toggle shows yearly pricing: `priceMonthly × 10` with "(save 2 months)" label.
- Current plan is highlighted.
- "Subscribe" button calls `CreateCheckoutSession` and redirects to Stripe Checkout URL.
- Disabled state + spinner while checkout session is being created.
- "Cancel" returns to `/billing`.

#### C10. StudioProfilePage

Already complex. Verify these specific items:

- Main form: name, city, location (lat/lng via `LocationPicker`). "Save" button.
- Slug section: current slug displayed. "Edit slug" button (one-time only).
  After slug is locked (`SlugLockedAt` set), the edit button is hidden and the slug
  is shown as read-only. Verify this logic matches the backend enforcement.
- Branding section: `BrandingSettingsCard`. Shows toggle for `ShowPlatformBranding`.
  Toggle is disabled if `Plan.AllowBrandingRemoval === false`. Shows a tooltip explaining why.
- QR code section: `QrCodeSection`. Shows QR preview image. "Download PNG" + "Download SVG" buttons.
- Referral code section: `ReferralCodeCard`. Shows active referral code (if any) + code string
  with copy button + "Generate new code" button.
- Embed code section: `EmbedCodeCard`. Shows the `<iframe>` embed snippet for the booking widget.
  Uses `VITE_PUBLIC_URL` env var (not `window.location.origin`).
- Notification preferences: `NotificationPreferencesCard`. Toggles for email/SMS notifications.
- Each sub-section loads independently (its own RTK Query call) — verify none crash if studio
  data isn't loaded yet (race between `useGetMyStudioQuery` in layout and page).

#### C11. Forms (Intake + Consent)

**IntakeFormListPage:**
- List of submitted intake forms (client name, submission date).
- Empty state when no forms.
- "View" links to `IntakeFormDetailPage`.

**IntakeFormDetailPage:**
- Shows: all form fields, client name, submission date.
- Read-only — owner/artist cannot edit intake forms.

**ConsentFormListPage / ConsentFormDetailPage:**
- Same structure as intake forms.
- Shows: signed date, client name, appointment reference.
- Consent form content (terms) rendered as formatted text.

#### C12. DepositRuleListPage + CreateDepositRulePage + DepositRuleDetailPage

**DepositRuleListPage:**
- Shows rule name, type (Percentage/Fixed), value.
- "Create rule" button → `/deposit-rules/new`.
- Empty state: "No deposit rules. Create one to collect deposits from clients at booking."
- Each rule links to its detail page.

**CreateDepositRulePage / DepositRuleDetailPage:**
- Fields: name, type (selector: Percentage | Fixed Amount), value (number input).
- If type = Percentage: validates `0 < value ≤ 100`.
- If type = Fixed: validates `value > 0`.
- On success: navigates to `/deposit-rules`.
- Delete button on detail page: confirm step. Disabled if rule in use.

#### C13. Notifications

**NotificationBell:**
- Shows unread count badge when notifications are unread.
- Clicking opens a dropdown panel showing the latest notifications.
- "Mark all read" button.
- "View all" link → `/notifications`.
- Count badge disappears when all are read.

**NotificationLogListPage:**
- Full list of notifications: type, message snippet, date, read/unread indicator.
- "Mark all read" bulk action.
- Empty state.

**NotificationPreferencesCard:**
- Toggles for: `emailOnBooking`, `smsOnBooking`, `emailOnCashDeposit`, `smsOnCancellation`.
  (Check the actual field names in `notificationsApi.ts`.)
- Each toggle calls `updateNotificationPreferences` mutation on change.
- Loading state during save (disable toggles, show spinner).

---

### Layer D — Test Suite Completeness

After fixing bugs in each layer, ensure every test file has minimum coverage.
Add missing tests — do not skip.

#### D1. SchedulePage.test.tsx

Required tests:
- Renders loading skeleton
- Renders week label with correct date range
- Previous week button changes week
- Next week button changes week
- Today button restores current week
- Empty week shows "No appointments" state
- Appointments are grouped by day
- Appointment card is a link to `/appointments/:id`

#### D2. AppointmentDetailPage.test.tsx

Required tests:
- Renders appointment details (date, artist, client, status)
- Pending state: shows Confirm and Cancel buttons
- Confirmed state: shows Complete, No-Show, Reschedule, Cancel buttons
- Completed state: no action buttons
- Confirm calls mutation and updates status badge
- Cancel shows confirmation step before calling mutation
- Complete shows confirmation step
- CashDepositConfirmButton shows for CashPending payment
- "Add to calendar" button triggers ICS download

#### D3. ArtistListPage.test.tsx + ArtistDetailPage.test.tsx

Required tests (ArtistListPage):
- Renders artist list with names
- Loading skeleton
- Empty state
- Search by name
- "Add Artist" link to `/artists/new`

Required tests (ArtistDetailPage):
- Renders artist name, specialty, rate
- Schedule section shows working hours
- Time-off section shows entries
- Add time-off form calls mutation
- Delete artist shows confirm step
- Delete disabled (409 from backend) shows error message

#### D4. ClientDetailPage.test.tsx + BodyMap.test.tsx

Required tests (ClientDetailPage):
- Renders name, email
- Profile section with allergies and medical notes
- Edit profile form calls upsertProfile mutation
- Tattoo history section shows records
- Portable profile toggle calls opt-in mutation

Required tests (BodyMap):
- Renders SVG body outline
- Clicking a zone toggles its active state
- Save calls updateBodyMap mutation
- Each zone has aria-label

#### D5. DesignDetailPage.test.tsx

Required tests:
- Renders title, client name, status badge
- InReview status shows Approve and Request Changes buttons
- Approve calls mutation and updates status
- Share token section: Generate shows token URL
- Revoke token shows confirm step
- Draft status shows "Upload revision" link

#### D6. PaymentListPage.test.tsx + CashDepositConfirmButton.test.tsx

Required tests (PaymentListPage):
- Renders payments list
- CashPending payments show CashDepositConfirmButton inline
- Status filter shows correct subset
- Empty state

Required tests (CashDepositConfirmButton):
- Renders client name and amount
- Click opens confirm step
- Confirm calls mutation
- Shows spinner during mutation
- On success: toast appears

#### D7. StudioProfilePage.test.tsx

Required tests:
- Renders studio name and city
- Save form calls updateMyStudio mutation
- Slug section shows current slug
- Slug edit button hidden after slug is locked
- BrandingSettingsCard renders toggle
- Toggle disabled when plan doesn't allow removal
- QrCodeSection renders QR preview and download buttons
- ReferralCodeCard shows copy button

#### D8. DepositRuleListPage.test.tsx + CreateDepositRulePage.test.tsx

Required tests:
- Renders rule list
- Empty state
- Create form validates percentage > 0 and ≤ 100
- Create form validates fixed amount > 0
- Create succeeds and navigates to list
- Delete shows confirm step

---

## Phase 1 Exit Condition

```
dotnet build   → 0 errors, 0 warnings
pnpm build     → 0 TypeScript errors
dotnet test    → All green
pnpm test      → All green
```

Do not exit Phase 1 until all four commands are clean.

---

# PHASE 2 — POLISH TO FINISHED PRODUCT

Phase 2 evaluates the owner section as a product manager would before a real beta launch.
Treat every owner screen as if a real studio owner will open it tomorrow. Go through each
criterion and implement what's missing.

---

## P1. Navigation & Layout

### P1.1 Document titles
Every owner page must set a descriptive browser tab title. Use `useDocumentMeta()`.
If the hook doesn't exist, create it as `frontend/src/shared/utils/useDocumentMeta.ts`:
```ts
import { useEffect } from "react";
export function useDocumentMeta(title: string) {
  useEffect(() => { document.title = title; }, [title]);
}
```
Required titles:
- Dashboard:        "Dashboard — Pena e Artë"
- Schedule:         "Schedule — Pena e Artë"
- Artists:          "Artists — Pena e Artë"
- Artist detail:    "{firstName} {lastName} — Artists — Pena e Artë"
- Clients:          "Clients — Pena e Artë"
- Client detail:    "{firstName} {lastName} — Clients — Pena e Artë"
- Designs:          "Designs — Pena e Artë"
- Design detail:    "{title} — Designs — Pena e Artë"
- Payments:         "Payments — Pena e Artë"
- Billing:          "Billing — Pena e Artë"
- Studio Settings:  "Studio Settings — Pena e Artë"
- Forms (intake):   "Intake Forms — Pena e Artë"
- Forms (consent):  "Consent Forms — Pena e Artë"
- Deposit Rules:    "Deposit Rules — Pena e Artë"
- Notifications:    "Notifications — Pena e Artë"

### P1.2 Mobile nav overflow
The owner nav has 8 items. On screens < 768px they overflow. Add:
```tsx
<nav className="ml-6 flex items-center gap-1 overflow-x-auto scrollbar-none shrink min-w-0">
```
On very small screens (< 480px), consider abbreviating "Studio Settings" → "Settings" in
the nav label only (keep the route and aria-label unchanged).

### P1.3 Per-route error boundaries
Wrap each major route element with `<ErrorBoundary>` in `router.tsx`, the same way the
issuer routes are wrapped. This prevents a crash in one feature from killing the whole app:
```tsx
{ index: true, element: <ErrorBoundary><DashboardPage /></ErrorBoundary> },
{ path: "schedule", element: <ErrorBoundary><SchedulePage /></ErrorBoundary> },
// ... etc for every owner route
```

### P1.4 Back navigation on detail pages
Every detail page (AppointmentDetailPage, ArtistDetailPage, ClientDetailPage,
DesignDetailPage, TattooRecordDetailPage) should have a `← Back to {list}` link
at the top using `Link` from react-router-dom. This is a standard SaaS pattern.
Example:
```tsx
<Link to="/clients" className="flex items-center gap-1.5 text-sm text-muted-foreground hover:text-foreground mb-4">
  <ChevronLeft className="h-4 w-4" />
  Back to clients
</Link>
```

---

## P2. Dashboard Polish

### P2.1 Fix the "Book Appointment" route
The "Book Appointment" button navigates to `/appointments/new` which doesn't exist.
Fix it to navigate to `/schedule` (or implement the route if preferred).

### P2.2 SetupChecklist
Read `SetupChecklist.tsx` in full. Verify it tracks the real setup steps for a new studio:
1. Add your first artist
2. Set your studio location
3. Set up a deposit rule
4. Complete your studio profile (name + city)
5. Add your Instagram handle (optional)

Each step should check the actual data (from RTK Query) to determine completion:
- Step 1: `artists.length > 0`
- Step 2: `studio.latitude !== null`
- Step 3: `depositRules.length > 0`
- Step 4: `studio.name && studio.city`
- Step 5: `studio.instagramHandle !== null` (optional — show as bonus step)

If the checklist has placeholder data or hardcoded `true`/`false` values, fix it to
use real data.

The checklist should hide itself (return null) when ALL required steps are complete.

### P2.3 Revenue summary on dashboard
Add a simple "This week's revenue" card below the 3 KPI cards:
```
Revenue this week   €X.XX
  from X paid appointments
```
Source: filter `useGetPaymentsQuery` for the current week date range and sum `amount`
where `status === "Paid"`. Show a skeleton while loading. Show `€0.00` if no paid payments.

### P2.4 Next appointment CTA
If there are appointments today, highlight the NEXT appointment (first one after current time)
with a subtle top position or different background. This is a tattoo studio — the owner
should immediately see who's coming in next.

In `TodaySection`, find the appointment with the earliest `date > now` and render it with
`bg-muted/60 rounded-lg` border or similar visual emphasis.

---

## P3. Schedule Polish

### P3.1 Appointment creation from schedule
The owner needs to be able to create a new appointment from the schedule view.
Verify `BookAppointmentForm` is accessible from `SchedulePage` (e.g., a "+ New" button
that opens the form inline or in a Dialog). If the form is only on `BookPage` (client
route), copy or extract a shared form component the owner can also use.

### P3.2 Today's day header highlight
In `SchedulePage`, the current day header (e.g., "Tuesday 1 Jul") should be visually
distinct. Add `font-semibold text-violet-500` to today's day heading.

### P3.3 ICS / Calendar download
Verify the "Add to calendar" feature exists in `AppointmentDetailPage`. If not present,
implement it:
1. Backend: `GET /api/v1/appointments/{id}/ics` → returns a `.ics` file.
   Use the standard iCal format:
   ```
   BEGIN:VCALENDAR\nVERSION:2.0\nBEGIN:VEVENT\n
   DTSTART:{startISO}\nDTEND:{endISO}\n
   SUMMARY:{clientName} @ {artistName}\n
   DESCRIPTION:{notes}\n
   END:VEVENT\nEND:VCALENDAR
   ```
2. Frontend: button triggers a `fetch` call to the endpoint and creates a download link.
   Use a plain event handler (not `useEffect`).

---

## P4. Artists Polish

### P4.1 Artist profile image upload
`ArtistDetailPage` shows an initials avatar when `profileImageUrl` is null.
Verify the edit form allows uploading a profile image via `FileUploadField`.
If it doesn't exist, add an "Upload photo" section using the same R2 upload flow as
portfolio images. Calls `UpdateArtistCommand.ProfileImageUrl`.

### P4.2 Portfolio management from ArtistDetailPage
The artist's portfolio images should be manageable from `ArtistDetailPage`:
- Grid of portfolio images (thumbnails).
- "+ Add image" button → `FileUploadField` → `UpdateArtistPortfolioCommand`.
- Delete button on each image (with confirm step).
- Images are referenced on the public portfolio (`/artist/{slug}`).

### P4.3 Artist schedule inline edit
`ArtistDetailPage` schedule section should be editable inline (not navigate away).
Show a table of Mon–Sun with start/end time pickers. "Save schedule" calls `UpsertArtistSchedule`.
If the schedule is displayed but editing requires a separate page, consolidate it.

---

## P5. Clients Polish

### P5.1 Client appointments tab
`ClientDetailPage` should show the client's appointment history at this studio:
source: filter `useGetAppointmentsQuery` by `clientId` (if the endpoint supports it).
If `clientId` filtering isn't supported by `GetAppointmentsQuery`, add it.
Show a simple list: date, artist, status, deposit status.

### P5.2 PortableProfile toggle help text
The `PortableProfileToggle` should explain what portable profiles do in plain language:
"Allow other studios on the platform to see your tattoo history when you visit them.
Your personal information is never shared — only your tattoo placement history."
This reduces client confusion about the opt-in.

### P5.3 Tattoo record image gallery
`TattooRecordDetailPage` should display images in a proper gallery with lightbox (shadcn Dialog,
no new package). If images are just a list of `<img>` tags, convert to a grid with
onClick opening a fullscreen dialog.

---

## P6. Designs Polish

### P6.1 Design approval email notification
When a client clicks "Approve" or "Request changes" on a design, verify the backend
fires a notification to the artist. Check `ApproveDesignHandler` and `RequestDesignChangesHandler`
for `SendDesignApprovalNotification` Hangfire job. If missing, add it.

### P6.2 Share link uses correct public URL
`ShareDesignButton` should generate the link as:
```
`${import.meta.env.VITE_PUBLIC_URL ?? window.location.origin}/share/${token}`
```
NOT just `window.location.origin/share/...` because when embedded on a third-party site,
origin would be wrong. Verify and fix.

### P6.3 Revision history with dates
Each revision in `DesignDetailPage` should show its upload date. The revision entity
should have `UploadedAt`. If missing in the backend DTO, add it to `DesignRevisionDto`.

---

## P7. Payments Polish

### P7.1 Session splits validation UX
`SessionSplitsEditor` must show a live running total as percentages are typed.
When the total ≠ 100%, show a warning: "Total is X% — must equal 100%." Disable
the save button until total === 100.

### P7.2 Payment status badges
`PaymentListPage` status badges should use consistent colors:
- `CashPending` → amber
- `Paid` → green
- `Failed` → red
- `Refunded` → muted/blue
Verify these match the `PaymentStatus` enum values.

### P7.3 Export payments (stretch goal)
If time permits: add a "Export CSV" button on `PaymentListPage` that downloads
a CSV of all payments currently in view. Build the CSV in the browser:
```ts
const csv = [headers, ...rows.map(r => [...fields])].map(r => r.join(",")).join("\n");
const blob = new Blob([csv], { type: "text/csv" });
const url  = URL.createObjectURL(blob);
// trigger download
```
This is a browser side-effect (URL.createObjectURL + click) — no `useEffect` needed,
it lives in an event handler.

---

## P8. Billing Polish

### P8.1 Yearly plan pricing on SubscribePage
Verify `SubscribePage` shows the yearly discount prominently:
- Monthly price: "€X / month"
- Yearly price: "€Y / year (save 2 months)"
  where `Y = priceMonthly × 10`.
- Yearly option should be the default if available (encourage annual subscriptions).
- Show `SAVE 17%` badge on the yearly card.

### P8.2 Billing page page title + trial days remaining
`BillingPage` in Trialing state shows "Trial ends in X days" — verify `daysUntil` doesn't
return a negative number if the trial has expired (it uses `Math.max(0, ...)` already ✓).
Verify the page has `useDocumentMeta("Billing — Pena e Artë")`.

---

## P9. Studio Settings Polish

### P9.1 Instagram handle display
`StudioProfilePage` should show the Instagram handle field in the main form.
If it's missing, add it: `instagramHandle?: string` (optional).
Strip leading `@` on save. Display as `@handle` in the form.

### P9.2 Phone number field
Add `phoneNumber?: string` field to the studio profile form.
Displayed in the public studio portfolio (`StudioPortfolioPage` sidebar) — verify
`GetMyStudio` response includes both `instagramHandle` and `phoneNumber`.
If missing from `UpdateMyStudioRequest` contract, add them.

### P9.3 Referral code copy button
`ReferralCodeCard` should have a copy-to-clipboard button next to the code string.
```tsx
<button
  onClick={() => navigator.clipboard.writeText(code).then(() => toast.success("Copied!"))}
  aria-label="Copy referral code"
>
  <Copy className="h-4 w-4" />
</button>
```
No `useEffect` needed — clipboard write is in the event handler.

### P9.4 Embed code preview
`EmbedCodeCard` should show a live preview of how the embed widget looks.
Use a small `<iframe>` preview inside the settings page:
```html
<iframe
  src={`${import.meta.env.VITE_PUBLIC_URL}/embed/${studio.slug}`}
  className="w-full h-64 rounded-lg border"
  title="Booking widget preview"
/>
```
If `VITE_PUBLIC_URL` is not set in `.env.example`, add it.

---

## P10. Global Polish Items

### P10.1 Toast notifications for all owner mutations

Every mutation in the owner section must fire a Sonner toast on success and on error.
Audit every mutation call in every owner component and add toasts where missing:

```
Create artist:       "Artist created"
Update artist:       "Artist updated"
Delete artist:       "{name} removed"
Confirm appointment: "Appointment confirmed"
Cancel appointment:  "Appointment cancelled"
Complete appointment: "Appointment marked complete"
Mark no-show:        "Marked as no-show"
Reschedule:          "Appointment rescheduled"
Create client:       "Client added"
Update client:       "Client updated"
Add tattoo record:   "Tattoo record saved"
Delete tattoo record: "Record deleted"
Create design:       "Design project created"
Upload revision:     "Revision uploaded"
Approve design:      "Design approved"
Request changes:     "Changes requested"
Create share token:  "Share link generated"
Revoke share token:  "Share link revoked"
Confirm cash:        "Cash payment of €X confirmed"
Create deposit rule: "Deposit rule created"
Update deposit rule: "Deposit rule updated"
Delete deposit rule: "Deposit rule deleted"
Update studio:       "Studio settings saved"
Update slug:         "Studio URL updated"
Update branding:     "Branding setting saved"
Update preferences:  "Notification preferences saved"

Error (generic):     error.data?.message ?? "Action failed. Try again."
```

### P10.2 Confirm dialogs for all destructive actions

Every destructive action must require confirmation before executing:
- Delete artist
- Cancel appointment
- Mark no-show
- Delete tattoo record
- Delete deposit rule
- Revoke design share token
- Delete portfolio image

Standard pattern (inline confirm, not a Dialog):
```tsx
{!confirmDelete
  ? <Button variant="destructive" size="sm" onClick={() => setConfirmDelete(true)}>Delete</Button>
  : (
    <div className="flex items-center gap-2">
      <span className="text-xs text-destructive">Are you sure?</span>
      <Button size="sm" variant="destructive" onClick={handleDelete}>Yes, delete</Button>
      <Button size="sm" variant="ghost" onClick={() => setConfirmDelete(false)}>Cancel</Button>
    </div>
  )}
```

### P10.3 Spinner + disabled state on all mutation buttons

Every button that triggers a mutation must be `disabled` during the mutation and show:
```tsx
{isLoading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <IconName />}
```
Audit every mutation button in the owner section.

### P10.4 Error states with retry on every async query

Every component that calls an RTK Query must handle `isError`. When `isError` is true,
show:
```tsx
<p role="alert" className="text-sm text-destructive">
  Failed to load {dataType}. <button onClick={refetch}>Try again</button>
</p>
```
Check every component that destructures `isError` from a query result and verify a UI
element renders for the error state.

### P10.5 Accessible form fields

Every form input must have:
- A `<Label htmlFor="...">` paired with `id="..."` on the input.
- `aria-describedby` pointing to the error message element when in error state.
- `aria-invalid="true"` on the input when there's a validation error.

Audit `CreateArtistPage`, `CreateClientPage`, `CreateDepositRulePage`, `CreateDesignPage`,
and `BookAppointmentForm`. Fix any inputs that are missing labels or aria attributes.

---

## Phase 2 Exit Condition

After completing all polish items:

1. Run `pnpm test` — all green.
2. Run `dotnet test` — all green.
3. Run `pnpm build` — no TypeScript errors.
4. Run `dotnet build` — no warnings.
5. Self-review checklist — mentally walk through every owner page and answer:
   - Does it have a document title?
   - Does every list have loading, error, and empty states?
   - Does every form have validation with inline errors on each field?
   - Does every mutation have a toast on success and error?
   - Does every destructive action have a confirmation?
   - Does every mutation button show a spinner while in-flight?
   - Does every detail page have a back link?
   - Does every query error have a retry button?
   - Are all interactive elements keyboard-accessible?
   - Do all action buttons meet 44px minimum touch target?
   If any answer is No, fix it before declaring done.

---

## Final Deliverable

When both phases exit cleanly, append an entry to `docs/claude/architecture.md`
under a new heading `## Owner QA Pass — 2026-07-01` listing:

1. Every bug found and fixed (one line each: file → bug → fix).
2. Every polish item implemented.
3. Any decisions made that aren't already in the Decisions Log — add them there too.
4. Any items skipped (with reason).

Keep it concise — this is a reference log, not a narrative.
