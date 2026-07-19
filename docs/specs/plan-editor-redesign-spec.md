# Spec: Plan editor redesign — dedicated edit page

**For:** frontend engineer implementing in `frontend/src/features/platform/`
**Replaces:** the inline expand-in-place editor on `PlanManagementPage.tsx` shown in the
current screenshot (Name / Yearly discount / Monthly price + Stripe price ID / Yearly
price + Stripe price ID / feature checkboxes / limit inputs, all inside a card that
pushes the grid open).

Assumption flagged for the engineer to confirm: this spec assumes a `Plan` entity/DTO
with the fields visible in the current UI (`Name`, `YearlyDiscountPercent`,
`MonthlyPriceEur`, `StripeMonthlyPriceId`, `YearlyPriceEur`, `StripeYearlyPriceId`,
`AllowBrandingRemoval`, `AllowApiAccess`, `PrioritySupport`, `MaxArtists`,
`MaxAppointmentsPerMonth`, `MaxNotificationsPerMonth`, `StorageGb`, `MaxLocations`) and
existing `platformApi` mutations for create/update/delete plan. `docs/claude/frontend.md`
lists `platform.types.ts` as only exporting `PlatformStatsResponse`,
`PlatformSubscriptionResponse`, `PlatformReferralCodeResponse`, and
`IndustryReportSummaryResponse` — no `PlanResponse` type is documented, so the engineer
should verify the actual current shape before wiring this up and adjust field names
accordingly.

---

## 1. Why change it

The current inline card editor has three problems: it reflows the whole grid when
opened (other plan cards jump position), it has no visual grouping (18 fields in one
flat column), and it uses raw checkboxes/number steppers that don't communicate
plan-tier semantics well. A dedicated page fixes all three without touching the data
model.

## 2. Routing

Add two routes under the existing `issuer`-only `platform` branch in `app/router.tsx`:

```tsx
{
  path: "plans",
  children: [
    { index: true, element: <PlanManagementPage /> },
    { path: "new", element: <PlanEditPage /> },
    { path: ":planId/edit", element: <PlanEditPage /> },
  ],
},
```

`PlanEditPage` handles both create and edit — branch on whether `useParams().planId`
is present. This matches the existing pattern of `RoleGuard allowedRoles={["issuer"]}`
wrapping the whole `platform` branch, so no new guard is needed.

## 3. Component structure

New file: `features/platform/components/PlanEditPage.tsx` (named export, matches
`PascalCase.tsx` convention).

```
PlanEditPage
├── breadcrumb: "Plans" (link to /platform/plans) / "{plan.name}" or "New plan"
├── page header: plan name as H1 (or "New plan"), Save + Cancel actions top-right
├── <form> (React Hook Form, shadcn/ui <Form> wrapper)
│   ├── Section card: Basic info
│   │     Name, Yearly discount %
│   ├── Section card: Pricing
│   │     Monthly price toggle → price (€) + Stripe monthly price ID (shown when on)
│   │     Yearly price toggle  → price (€) + Stripe yearly price ID (shown when on)
│   │     Read-only "suggested yearly" hint derived from monthly × 12 × (1 - discount)
│   ├── Section card: Feature flags
│   │     Allow branding removal / Allow API access / Priority support — shadcn/ui
│   │     <Switch>, not checkboxes
│   └── Section card: Usage limits
│         Artists, Appointments/mo, Notifications/mo, Storage (GB), Locations —
│         each with an "unlimited" checkbox that clears/disables the numeric input,
│         replacing today's "blank = unlimited" convention (which is not discoverable)
└── sticky footer (mobile) / inline header actions (desktop): Save, Cancel
```

Each "Section card" is a `<Card>` (shadcn/ui) with a `<CardHeader><CardTitle>` label —
this is the grouping the current design is missing. Two-column layout inside each card
on desktop (`grid grid-cols-2 gap-4`), single column below `md`.

## 4. Form handling

- React Hook Form + shadcn/ui `<Form>` / `<FormField>` primitives, per
  `docs/claude/conventions.md` (RHF is already the project standard).
- Client-side schema should mirror whatever the `CreatePlanValidator` /
  `UpdatePlanValidator` FluentValidation rules are on the backend — engineer should
  read those validators before writing the zod/RHF schema so error messages match.
- Unsaved-changes guard: block navigation away from the page (React Router v7
  `useBlocker`) if the form is dirty, with a confirm dialog. The old inline editor had
  an explicit ✕ cancel button next to Save with no dirty-check — this is a behavior
  upgrade, call it out in the PR description since it changes existing UX.

## 5. Data layer

Use the existing `platformApi` RTK Query slice (`features/platform/platformApi.ts`) —
do not create a new API slice, per `docs/claude/frontend.md` ("Do NOT add issuer
platform queries to `billingApi` or `studiosApi`").

- `PlanEditPage` in edit mode: `useGetPlanQuery(planId)` if a single-plan fetch
  endpoint exists, otherwise select from the already-cached `useGetPlansQuery()` result
  by ID (avoids a new endpoint if the list query already returns full plan objects, as
  the current inline editor implies).
- Save: `useCreatePlanMutation()` / `useUpdatePlanMutation()`, `invalidatesTags:
  ["Plan"]` (confirm actual tag name against the existing slice).
- On success: navigate back to `/platform/plans`, RTK Query cache invalidation
  refreshes the grid.
- On failure: surface FluentValidation 400 field errors by mapping them onto RHF field
  errors (`setError` per field), not a generic toast — this is a form with 13+ fields,
  a toast alone won't tell the issuer which field is wrong.

## 6. Update to `PlanManagementPage.tsx`

- Remove the inline expand/edit-in-card behavior entirely.
- Each plan card's edit icon becomes a `<Link to={`/platform/plans/${plan.id}/edit`}>`
  (or `navigate()` on click) instead of a local `expandedId` state toggle.
- "New plan" tile/button navigates to `/platform/plans/new` instead of inserting a new
  card into the grid.
- The card itself becomes read-only summary display only (name, member count, price,
  badges) — no form elements left on this page at all.

## 7. Edge cases to handle

- Plan with zero active studios/subscribers (`Free`, `Growth`, `Pro` in the current
  screenshot show 0 or 1) — deletion should still be blocked/warned if subscriber count
  > 0, same as today's delete icon presumably already guards against.
- Toggling "Monthly price" or "Yearly price" off after entering a Stripe price ID —
  decide whether to clear the ID or just hide the field; recommend hiding only
  (don't destroy data) and gray it out.
- Navigating directly to `/platform/plans/:planId/edit` for a `planId` that doesn't
  exist (bad URL, deleted plan) — show a 404-style inline state with a link back to
  `/platform/plans`, not a blank form.
- Concurrent edit: two issuer admins editing the same plan — out of scope for this
  pass unless the backend already returns a 409 on stale writes; if it does, surface
  that as a form-level error banner ("This plan changed since you opened it. Reload to
  see the latest values.").

## 8. Testing implications

Per `docs/claude/conventions.md`, Application-layer business logic needs tests — this
change is frontend-only and doesn't touch `Pena_e_Arte.Application`, so no new backend
tests are required unless the validators change. Frontend tests to add
(`describe`/`it`, per convention):

```
describe("PlanEditPage", () => {
  it("pre-fills all fields when editing an existing plan");
  it("submits create mutation when no planId param is present");
  it("submits update mutation when a planId param is present");
  it("maps a 400 validation response onto the matching form field");
  it("blocks navigation away when the form is dirty");
  it("hides Stripe price ID inputs when the corresponding price toggle is off");
});
```

## 9. Non-negotiables this change must still satisfy

- Endpoint(s) behind this page remain `IssuerOnly` — no change expected here since
  plan CRUD is already issuer-scoped, but confirm the create/update endpoints have
  `.RequireAuthorization("IssuerOnly")` before wiring the mutations.
- No `any` in the new TypeScript — type the form values and mutation payloads
  explicitly.
- No inline styles — Tailwind + shadcn/ui only, per `docs/claude/frontend.md`.
