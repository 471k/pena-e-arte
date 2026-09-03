# Overnight Prompt — Compliance + Payment-Flow Correctness (Tier 3)

> Feed this file directly to Claude Code (main **Pena e Artë - Engineering** project, full repo
> write access) as the task prompt. **Fully autonomous — no external dependency, no
> BLOCKING-MANUAL item anywhere in this prompt.** Can run any time, independently of the
> production-deploy and staging prompts in this series, ideally before or alongside them. Four
> independent, code-level fixes, verified against the live repo on 2026-09-03. Read this file in
> full before writing anything; each section below cites the exact current file contents this
> session found, so there should be no need to re-discover them from scratch — but re-read the
> cited files yourself before editing, since this doc is a snapshot and the surrounding code may
> have moved since.

**Date logged:** 2026-09-03
**Requested by:** Phi
**Origin:** Engineering-consultation gap audit.

**Checkpoint before starting:**
```bash
git status                     # must be clean before starting
git checkout main && git pull
git checkout -b fix/deposit-consent-audit-compliance
git commit --allow-empty -m "checkpoint: before compliance + payment-flow correctness work"
```

Per this project's rule 6 (industry-standard benchmark) and rule 7 (Help stays in sync), **every
user-facing change below must update `frontend/src/features/help/helpContent.ts` and, where the
affected flow has an onboarding-tour step, the matching file in
`frontend/src/features/help/tours/`** — §5 lists exactly which articles/tours are affected.

---

## 1. Root cause shared by §2 and §3.1 — read this first

`Pena_e_Arte.Infrastructure/Services/NullPaymentProvider.cs` is the DI-registered
`IPaymentProvider` today (confirmed) — every method throws `InvalidOperationException("No
payment provider is configured...")`, by design, until a real POK-based provider lands
(separate ticket, not this prompt). `IPaymentProvider.Capabilities` already models this
correctly: `NullPaymentProvider.Capabilities` returns `SupportsAuthCapture: false` and the rest
all-false/empty. **Nothing today exposes that capability to the frontend.** The frontend's own
signal for "is card payment available" is `PaymentMethodSelector.tsx`'s `!!import.meta.env.
VITE_STRIPE_PUBLISHABLE_KEY` check — a client-side proxy that is **wrong** the moment a real
Stripe *test-mode* publishable key is configured (e.g. staging, per this project's staging
prompt reusing local dev's test key pair) while the backend still has no working provider behind
it. In that state: the key is present, `stripePromise` is non-null, `CardTab` renders, calls
`useCreateDepositPaymentMutation` → `CreateDepositPaymentCommand` → `stripePayments.
CreatePaymentHoldAsync(...)` → throws → the mutation surfaces a generic 500 to the client
instead of a clear "unavailable" message. `DepositCheckoutPage.tsx`'s standalone `/pay/:paymentId`
route has the identical exposure if it's ever reached with a stale `ClientSecret` from before
the provider was swapped to `NullPaymentProvider`.

**Fix: add a real backend capability signal, gate the UI on that instead of (in addition to) the
env-var proxy.**

### 1.1 — New query + endpoint

New file `Pena_e_Arte.Application/Payments/Queries/GetPaymentCapabilitiesQuery.cs`:
```csharp
public record GetPaymentCapabilitiesQuery() : IRequest<PaymentCapabilitiesResponse>;

public class GetPaymentCapabilitiesHandler(IPaymentProvider paymentProvider)
    : IRequestHandler<GetPaymentCapabilitiesQuery, PaymentCapabilitiesResponse>
{
    public Task<PaymentCapabilitiesResponse> Handle(GetPaymentCapabilitiesQuery query, CancellationToken ct) =>
        Task.FromResult(new PaymentCapabilitiesResponse(
            CardPaymentsAvailable: paymentProvider.Capabilities.SupportsAuthCapture));
}
```
New response in `Pena_e_Arte.Contracts/Responses/` (match this project's existing record-response
convention — check a neighboring file like `PaymentIntentResponse` for exact style):
```csharp
public record PaymentCapabilitiesResponse(bool CardPaymentsAvailable);
```
New endpoint in `Pena_e_Arte.API/Endpoints/PaymentEndpoints.cs`, inside the existing
`/api/v1/payments` group (already `RequireAuthorization()` at the group level — this doesn't
leak tenant data, any authenticated role may read it, so no narrower policy needed, matching
this endpoint file's existing mix of policies):
```csharp
group.MapGet("/capabilities", GetPaymentCapabilities);
```
with a handler method following the same shape as the file's other `GetX` handlers. No
FluentValidation validator needed (no request body/params).

### 1.2 — Frontend: fetch and gate on it

In `frontend/src/features/payments/paymentsApi.ts`, add `useGetPaymentCapabilitiesQuery`
(RTK Query `GET /api/v1/payments/capabilities`, matching this file's existing query
definitions' style). This is small, cacheable, authenticated data — no special cache
invalidation tags needed beyond RTK Query's defaults.

In `PaymentMethodSelector.tsx`'s `CardTab` (`frontend/src/features/payments/components/
PaymentMethodSelector.tsx`): call `useGetPaymentCapabilitiesQuery()` alongside the existing
`stripeKey` check. Card tab UI logic becomes: unavailable if **either** `!stripePromise`
(existing check — no publishable key at all) **or** `capabilities?.cardPaymentsAvailable ===
false` (new check — backend has no working provider regardless of key presence). Only call
`useCreateDepositPaymentMutation`'s effect (the `useEffect` that fires `createDeposit`) when
both are true. Replace the existing "Card payments are not configured (missing Stripe
publishable key)" message with wording that covers both cases without implying a config
mistake when it's really a business-state fact: **"Card payments are temporarily unavailable.
Use the Cash option below, or contact the studio directly."** Keep the existing loading state
while the capabilities query is in flight (don't flash the unavailable message before the query
resolves).

In `DepositCheckoutPage.tsx` (`frontend/src/features/payments/components/
DepositCheckoutPage.tsx`): this route only renders its Stripe form when
`data?.clientSecret` is present from `useGetPaymentClientSecretQuery`. Add the same
`useGetPaymentCapabilitiesQuery()` check before rendering the `<Elements>`/`<CheckoutForm>`
block — if `cardPaymentsAvailable === false`, render a clear message ("Card payments are
temporarily unavailable for this link. Please contact the studio.") instead of attempting
`stripe.confirmPayment` against what may be a stale/dead client secret. This is a defensive
guard for a route that may be unreachable under `NullPaymentProvider` today but shouldn't ever
render a form it can't complete if that changes.

In `CreatePaymentIntentPage.tsx` (`frontend/src/features/payments/components/
CreatePaymentIntentPage.tsx`, the **owner-facing** ad-hoc payment-link creator — same root
cause: it calls `useCreatePaymentIntentMutation` → `CreatePaymentIntentCommand` →
`stripePayments.CreatePaymentHoldAsync` → throws): gate the "create a card payment link"
action on the same `useGetPaymentCapabilitiesQuery()` result. When unavailable, disable that
action with the same "temporarily unavailable" wording and point the owner at the cash-declare
path instead (already present in this component per its `useDeclareCashDepositMutation`
import). This wasn't named explicitly in the source audit's 3.1, but it's the identical bug on
the identical root cause in the one other place `CreatePaymentHoldAsync` is called from a
client-visible flow — fixing 3.1 without this would leave an owner able to generate a payment
link that can never be completed, immediately reproducing the exact bug 3.1 exists to fix.

---

## 2. Intake-form consent (source audit §3.2)

`SubmitIntakeFormPage.tsx` (`frontend/src/features/forms/components/SubmitIntakeFormPage.tsx`)
collects free-text medical/tattoo-history data (`formData: z.string().min(10)`) with **zero**
consent UI — confirmed, no checkbox, no consent text, nothing. `ConsentTemplateKind`
(`Pena_e_Arte.Domain/Enums/ConsentTemplateKind.cs`) has exactly two values today,
`AppointmentConsent` and `CrossTenantProfileSharing` — neither covers this submission. Real
Law 124/2024 (Albania) / GDPR Art. 9 exposure until this has its own consent, tied to a real
`ConsentTemplate` record the same way appointment booking already does it (`SignConsentFormCommand`,
`GetActiveConsentTemplateQuery`).

**Do not reuse the `ConsentForm` entity as-is** — it's `AppointmentId` is a non-nullable `Guid`
(`Pena_e_Arte.Domain/Entities/ConsentForm.cs`) and it's wired to a signature/PDF-generation flow
(`SignConsentFormHandler`'s `TryGeneratePdfAsync`) that doesn't apply here (intake forms don't
generate PDFs, and `IntakeForm.AppointmentId` is itself optional — a form can exist with no
appointment at all). Making `ConsentForm.AppointmentId` nullable to shoehorn this in would touch
every other `ConsentForm` consumer for no benefit. Instead, add lightweight consent-acceptance
fields directly to `IntakeForm`, mirroring `ConsentForm`'s own "immutable snapshot" pattern:

### 2.1 — Domain

Add to `ConsentTemplateKind`:
```csharp
/// <summary>Consent to submit free-text medical/tattoo-history data via the intake form
/// (Law 124/2024 (Albania) / GDPR Art. 9 special-category data).</summary>
IntakeFormConsent,
```

Add to `Pena_e_Arte.Domain/Entities/IntakeForm.cs`:
```csharp
public Guid? ConsentTemplateId { get; set; }
public string? ConsentTextSnapshot { get; set; }
public DateTime? ConsentedAt { get; set; }
```
Same nullability reasoning as `ConsentForm`'s own fields — forms submitted before this ships
have none of these set; don't backfill.

### 2.2 — Application

Generalize `GetActiveConsentTemplateQuery` (`Pena_e_Arte.Application/ConsentForms/Queries/
GetActiveConsentTemplateQuery.cs`) to take the kind as a parameter rather than hardcoding
`ConsentTemplateKind.AppointmentConsent`:
```csharp
public record GetActiveConsentTemplateQuery(ConsentTemplateKind Kind) : IRequest<ConsentTemplateResponse>;
```
Update its handler to filter on `query.Kind` instead of the hardcoded constant, and update
`ConsentTemplateResponse`'s `Kind` field to `query.Kind.ToString()`. Update the one existing
call site (`SignConsentFormPage.tsx`'s `useGetActiveConsentTemplateQuery()` call and the RTK
Query definition in `consentFormsApi.ts`) to pass `{ kind: "AppointmentConsent" }` explicitly —
grep for every call site before assuming there's only one.

Update `SubmitIntakeFormCommand` (`Pena_e_Arte.Application/IntakeForms/Commands/
SubmitIntakeFormCommand.cs`) to require and record consent:
- Add `bool ConsentAccepted` to `SubmitIntakeFormRequest` (`Pena_e_Arte.Contracts/Requests/`).
- Add a FluentValidation validator (or extend the existing one — check
  `Pena_e_Arte.Application/IntakeForms/Validators/` first) requiring `ConsentAccepted == true`,
  matching this project's rule against shipping an endpoint without one.
- In the handler, resolve the active `IntakeFormConsent` template the same way
  `SignConsentFormHandler` does (studio's own active template, else platform default — reuse
  `ConsentTemplateResolver.ResolveActive`, don't reimplement the resolution logic), and stamp
  `form.ConsentTemplateId`, `form.ConsentTextSnapshot = template?.BodyText`, `form.ConsentedAt
  = DateTime.UtcNow` before `db.SaveChangesAsync`.

New EF Core migration (`Pena_e_Arte.Infrastructure`, follow this project's existing naming —
most recent precedent `20260831142217_AddBookingIntakeGuestBookingAndAttachmentCategory`):
```bash
dotnet ef migrations add AddIntakeFormConsent --project Pena_e_Arte.Infrastructure
```
Adds the three nullable columns to `IntakeForm` and the new enum value (enums stored as strings
or ints — check the existing `ConsentTemplateKind` column's configuration in
`AppDbContext`/entity configuration before assuming which; match whatever's already there).

### 2.3 — Frontend

In `SubmitIntakeFormPage.tsx`: fetch `useGetActiveConsentTemplateQuery({ kind: "IntakeFormConsent"
})`, render the resolved `bodyText` (fall back to reasonable default consent copy if no template
is configured for either the studio or the platform — same "signing still works, generic copy"
fallback `GetActiveConsentTemplateHandler` already documents for the appointment-consent case),
add a required checkbox ("I consent to sharing this medical/health information with the studio")
above the submit button, wire it into the Zod schema (`consentAccepted: z.literal(true, { errorMap:
() => ({ message: "You must consent before submitting" }) })` or this codebase's existing
equivalent pattern — check how other required-checkbox validations are written elsewhere in the
frontend first), and pass `consentAccepted: values.consentAccepted` through to the mutation call.

---

## 3. Refund and cash-confirmation audit logging (source audit §3.3)

Confirmed via grep: of every command in `Pena_e_Arte.Application/Payments/Commands/`, only
`UpdateSessionSplitsCommand` implements `IAuditableCommand`. `RefundPaymentCommand` and
`ConfirmCashDepositCommand` do not — any refund or cash-payment confirmation happens today with
zero audit trail. Small, mechanical fix — the pattern already exists to copy verbatim from
`UpdateSessionSplitsCommand.cs`.

### 3.1 — New constants

Add to `Pena_e_Arte.Domain/Constants/AuditActions.cs`:
```csharp
public const string PaymentRefunded = "Payment.Refunded";
public const string CashDepositConfirmed = "Payment.CashDepositConfirmed";
```
`AuditTargetTypes.Payment` already exists — no new target type needed.

### 3.2 — RefundPaymentCommand

In `Pena_e_Arte.Application/Payments/Commands/RefundPaymentCommand.cs`, change the record to:
```csharp
public record RefundPaymentCommand(Guid PaymentId, decimal? Amount)
    : IRequest<PaymentResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.PaymentRefunded;
    public string AuditTargetType => AuditTargetTypes.Payment;
    public Guid AuditTargetId => PaymentId;
}
```
This command's endpoint is `OwnerOnly` (confirmed in `PaymentEndpoints.cs`:
`.RequireAuthorization("OwnerOnly")`), so `AuditStudioId` can be left at its default (null) —
`AuditLogBehavior` falls back to the caller's `ICurrentTenant.StudioId`, exactly matching
`UpdateSessionSplitsCommand`'s own comment explaining the same thing. Copy that comment.

### 3.3 — ConfirmCashDepositCommand

In `Pena_e_Arte.Application/Payments/Commands/ConfirmCashDepositCommand.cs`:
```csharp
public record ConfirmCashDepositCommand(Guid PaymentId) : IRequest<PaymentResponse>, IAuditableCommand
{
    public string AuditAction => AuditActions.CashDepositConfirmed;
    public string AuditTargetType => AuditTargetTypes.Payment;
    public Guid AuditTargetId => PaymentId;
}
```
This endpoint is `ArtistAndAbove` (confirmed), tenant-scoped the same way — same default-null
`AuditStudioId` reasoning applies.

Verify `AuditLogBehavior` (`Pena_e_Arte.Application/Common/Behaviors/AuditLogBehavior.cs`) picks
both up automatically via the `IAuditableCommand` MediatR pipeline behavior with no other wiring
needed — confirm by reading it, don't assume from `UpdateSessionSplitsCommand`'s working example
alone, since this prompt doesn't want to discover a second registration step was needed only
after shipping.

---

## 4. Frontend test for §1's fix

Whatever §1.2 lands as (the "temporarily unavailable" state, the capabilities-gated card tab),
add a focused component test matching this codebase's existing convention — the bar set by
`StagingBanner.test.tsx` from the staging-environment work: a handful of render-state
assertions, not exhaustive coverage. Concretely, extend `frontend/src/features/payments/
__tests__/PaymentMethodSelector.test.tsx`:
- a test that mocks `useGetPaymentCapabilitiesQuery` to return `{ cardPaymentsAvailable: false
  }` and asserts the card tab shows the "temporarily unavailable" message and never fires
  `useCreateDepositPaymentMutation`.
- a test that the existing "card tab creates the deposit intent" test still passes with
  `cardPaymentsAvailable: true` mocked (update its existing mock setup rather than leaving the
  new query unmocked, which would make the test suite's mock-server/MSW handlers — check which
  this codebase uses — reject the new endpoint call).

Also extend `frontend/src/features/payments/__tests__/DepositCheckoutPage.test.tsx` with one
test for the new "unavailable" guard when `cardPaymentsAvailable: false`.

Add one new test file for the intake-form consent checkbox (§2.3), matching
`SubmitIntakeFormPage`'s existing test conventions if a `__tests__/SubmitIntakeFormPage.test.tsx`
already exists (check first — extend it if so, create it if not): asserts the submit button
is disabled/the form rejects submission when the consent checkbox is unchecked, and that the
active template's `bodyText` renders.

Backend: add unit tests for `RefundPaymentHandler` and `ConfirmCashDepositHandler` asserting the
`AuditAction`/`AuditTargetType`/`AuditTargetId` values resolve correctly — mirror whatever test
already exists for `UpdateSessionSplitsCommand`'s audit fields (check
`tests/Pena_e_Arte.UnitTests/` for it first, copy its shape).

---

## 5. Help-sync obligations (CLAUDE.md rule 7 — not optional)

- `frontend/src/features/help/helpContent.ts`: the existing `client-deposit-pay` article
  (id confirmed present) needs a note about the "temporarily unavailable" card-payment state and
  that Cash remains available — add a step/tip, don't rewrite the whole article. The
  `client-intake-submit` article needs a step covering the new required consent checkbox.
- `frontend/public/user-manual/index.html`: same two sections, same content, kept in sync per
  this project's three-surface rule — find the corresponding sections by searching for the
  existing deposit-payment and intake-form content there first, don't duplicate a section that
  already exists.
- `frontend/src/features/help/tours/clientTour.ts`: check whether any step references the
  deposit-payment or intake-form pages; if so, update its copy to match. If no step touches
  either flow today, say so explicitly in the final summary rather than silently skipping this
  file — CLAUDE.md rule 7 requires checking, not assuming.

---

## 6. Explicitly out of scope

- Wiring a real payment provider (POK or otherwise) — separate ticket per ADR-0001, not this
  prompt's job. §1's fix makes the *absence* of a provider behave correctly; it does not add one.
- Extending `ConsentForm` (the appointment/signature/PDF flow) itself — §2 deliberately avoids
  touching it, see the reasoning there.
- A DPIA/DPO threshold determination for whether this consent change is itself sufficient under
  Albania Law 124/2024 — that's Tier 6 of the source audit, a human/legal decision, not
  something this session determines.
- Any change to `RefundPaymentCommand`'s or `ConfirmCashDepositCommand`'s actual business logic
  beyond adding the audit interface — this is an audit-trail fix, not a behavior change.

---

## 7. Final self-check

- [ ] `GET /api/v1/payments/capabilities` exists, is authenticated, and returns
      `CardPaymentsAvailable: false` today (confirm by hitting it against a running local API
      with the default `NullPaymentProvider` — don't just assume from reading the code).
- [ ] All three frontend surfaces (`PaymentMethodSelector`, `DepositCheckoutPage`,
      `CreatePaymentIntentPage`) gate on the new capability, not only the publishable-key
      presence check, and show clear "temporarily unavailable" copy rather than a raw error or
      a form that can't succeed.
- [ ] `ConsentTemplateKind.IntakeFormConsent` exists, the migration applies cleanly
      (`dotnet ef database update`), and `SubmitIntakeFormCommand` rejects a submission with
      `ConsentAccepted: false` via the validator (not a runtime exception).
- [ ] `GetActiveConsentTemplateQuery` was generalized to take `Kind`, and every existing call
      site was updated — grep confirms no remaining reference to the old hardcoded-kind
      behavior.
- [ ] `RefundPaymentCommand` and `ConfirmCashDepositCommand` both implement `IAuditableCommand`
      and a real refund/cash-confirmation in a test or local run produces an `AuditLogEntry` row.
- [ ] `pnpm test` and `dotnet test` both pass, including the new tests from §4.
- [ ] `pnpm build`/`tsc -b` was run, not just `vitest run` — this project's own memory notes
      that build/typecheck has caught real bugs the unit-test run alone missed before.
- [ ] §5's three Help surfaces were each explicitly checked and updated (or explicitly noted as
      needing no change, for the tour file) — not silently skipped.
- [ ] `docs/claude/architecture.md`'s Feature Module Map entries for Payments and Intake Forms
      (if they exist as separate rows) are updated to reflect the new capability query and
      consent requirement — check whether these features have their own Map rows first.
