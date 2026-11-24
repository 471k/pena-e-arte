# Frontend Instructions — React 19 + Redux Toolkit

> Load this file when working on anything inside `src/frontend/`.

---

## Project Layout

```
src/frontend/src/
├── app/
│   ├── store.ts                root Redux store
│   ├── router.tsx              React Router v7 setup + role guards
│   └── hooks.ts                typed useAppDispatch / useAppSelector
│
├── features/                   one folder per domain
│   ├── appointments/
│   │   ├── appointmentsApi.ts  RTK Query slice
│   │   ├── appointmentsSlice.ts local UI state if needed
│   │   ├── components/
│   │   └── index.ts            public exports only
│   ├── auth/
│   ├── clients/
│   ├── billing/
│   ├── designs/
│   └── notifications/
│
├── shared/
│   ├── components/             Button, Modal, Avatar, DataTable…
│   ├── hooks/                  useSignalR, useCurrentUser, usePermission
│   ├── utils/                  formatDate, formatCurrency, cn()
│   └── types/                  global TypeScript types
│
└── layouts/
    ├── ClientLayout.tsx
    ├── ArtistLayout.tsx
    ├── OwnerLayout.tsx
    └── AdminLayout.tsx
```

---

## Navigation (sidebar)

Every role layout (`Client`/`Artist`/`Owner`/`Admin`) renders the same vertical, Cloudflare-style
sidebar. Do not hand-roll a top nav in a layout, and do not add a flat `NavItem[]` — add entries to
the layout's `NavSection[]`.

```
shared/types/navItem.ts            NavItem (link) · NavGroup (expandable, has children) · NavSection (labelled block)
shared/components/SidebarNav.tsx   renders sections + groups; `collapsed` = icon rail, `touch` = 44px rows
shared/components/AppSidebar.tsx   desktop (lg+) sticky, scrollable aside; Collapse button + Ctrl/Cmd+B; persists "sidebar-collapsed"
shared/components/NavDrawer.tsx    below lg: hamburger + Sheet rendering the same SidebarNav
shared/hooks/useNavShell.ts        drawer open state + onboarding-tour reveal (pass to HelpMenu.onBeforeTourStep)
shared/utils/navSections.ts        withNavBadges / pruneNavSections / flattenNavItems / active-route helpers
layouts/artistNavSections.tsx      artist menu, shared by ArtistLayout and OwnerLayout's artist mode
```

Rules when adding a menu item:

- Put it in the category it belongs to (a `NavGroup` for 2+ related pages, a plain `NavItem` for a
  standalone destination) under a labelled `NavSection`. Keep the most-used destinations top-level.
- Layout header is a fixed `h-14`; `AppSidebar` sticks at `top-14` and is `100vh - 3.5rem` tall. Keep both in sync.
- A link that needs an onboarding-tour target keeps its `tourId` (`<role>-<thing>-nav`). The sidebar
  opens the group / expands the rail for that step automatically — the `-nav"]` selector suffix is
  what triggers it, so don't rename that convention.
- Live counts go through `withNavBadges(sections, { "<Item label>": count })`; a folded group shows
  the sum of its children.
- `end: true` on any item whose href is a prefix of a sibling's (e.g. `/clients/me` vs
  `/clients/me/payment-methods`), otherwise both highlight.

---

## Redux Store Structure

```typescript
// app/store.ts — actual slices as of 2026-06-11
export const store = configureStore({
  reducer: {
    auth:          authReducer,
    ui:            uiReducer,
    notifications: notificationsReducer,
    // RTK Query reducers (add new api slices here as features are built)
    [appointmentsApi.reducerPath]:  appointmentsApi.reducer,
    [clientsApi.reducerPath]:       clientsApi.reducer,
    [billingApi.reducerPath]:       billingApi.reducer,
    [studiosApi.reducerPath]:       studiosApi.reducer,
    [designsApi.reducerPath]:       designsApi.reducer,
    [authApi.reducerPath]:          authApi.reducer,
    [filesApi.reducerPath]:         filesApi.reducer,
    [intakeFormsApi.reducerPath]:   intakeFormsApi.reducer,
    [consentFormsApi.reducerPath]:  consentFormsApi.reducer,
    [depositRulesApi.reducerPath]:  depositRulesApi.reducer,
    [platformApi.reducerPath]:      platformApi.reducer,   // admin platform
  },
  middleware: (getDefault) =>
    getDefault()
      .concat(appointmentsApi.middleware)
      .concat(clientsApi.middleware)
      .concat(billingApi.middleware)
      .concat(studiosApi.middleware)
      .concat(designsApi.middleware)
      .concat(authApi.middleware)
      .concat(filesApi.middleware)
      .concat(intakeFormsApi.middleware)
      .concat(consentFormsApi.middleware)
      .concat(depositRulesApi.middleware)
      .concat(platformApi.middleware),
});

export type RootState   = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
```

Always use typed hooks from `app/hooks.ts`, never raw `useDispatch`/`useSelector`:
```typescript
export const useAppDispatch = () => useDispatch<AppDispatch>();
export const useAppSelector: TypedUseSelectorHook<RootState> = useSelector;
```

---

## RTK Query Pattern

One `createApi` call per domain. Keep in `features/<domain>/<domain>Api.ts`.

```typescript
// features/appointments/appointmentsApi.ts
export const appointmentsApi = createApi({
  reducerPath: "appointmentsApi",
  baseQuery: fetchBaseQuery({
    baseUrl: "/api/v1/",
    prepareHeaders: (headers, { getState }) => {
      const { token, tenantId } = (getState() as RootState).auth;
      if (token)    headers.set("Authorization", `Bearer ${token}`);
      if (tenantId) headers.set("X-Tenant-Id", tenantId);
      return headers;
    },
  }),
  tagTypes: ["Appointment"],
  endpoints: (builder) => ({
    getAppointments: builder.query<AppointmentResponse[], void>({
      query: () => "appointments",
      providesTags: ["Appointment"],
    }),
    createAppointment: builder.mutation<AppointmentResponse, CreateAppointmentRequest>({
      query: (body) => ({ url: "appointments", method: "POST", body }),
      invalidatesTags: ["Appointment"],
    }),
  }),
});

export const {
  useGetAppointmentsQuery,
  useCreateAppointmentMutation,
} = appointmentsApi;
```

---

## Auth Slice

```typescript
// features/auth/authSlice.ts
interface AuthState {
  user:     User | null;
  token:    string | null;
  tenantId: string | null;
  role:     Role | null;
}

const authSlice = createSlice({
  name: "auth",
  initialState: { user: null, token: null, tenantId: null, role: null } as AuthState,
  reducers: {
    setCredentials: (state, { payload }: PayloadAction<AuthPayload>) => {
      state.user     = payload.user;
      state.token    = payload.token;
      state.tenantId = payload.tenantId;
      state.role     = payload.role;
    },
    logout: () => ({ user: null, token: null, tenantId: null, role: null }),
  },
});
```

---

## Role-Based Route Guards

```typescript
// app/router.tsx
const router = createBrowserRouter([
  {
    path: "/",
    element: <RoleGuard allowedRoles={["client", "artist", "owner", "admin"]} />,
    children: [
      { path: "book",      element: <ClientLayout />,  ... },
      { path: "schedule",  element: <ArtistLayout />,  ... },
      { path: "dashboard", element: <OwnerLayout />,   ... },
      {
        path: "platform",
        element: <RoleGuard allowedRoles={["admin"]} />,
        children: [
          { index: true,           element: <AdminDashboardPage /> },
          { path: "studios",       element: <AdminStudioListPage /> },
          { path: "plans",         element: <PlanManagementPage /> },
          { path: "subscriptions", element: <SubscriptionOversightPage /> },
          { path: "referrals",     element: <PlatformReferralPage /> },
          { path: "reports",       element: <IndustryReportsPage /> },
        ],
      },
    ],
  },
  { path: "/login", element: <LoginPage /> },
]);

// After login, redirect by role:
// client  → /book
// artist  → /schedule
// owner   → /dashboard
// admin   → /platform   ← NOT /dashboard

// shared/hooks/usePermission.ts — use this for conditional UI rendering
export function usePermission(requiredRole: Role): boolean {
  const role = useAppSelector((s) => s.auth.role);
  return hasPermission(role, requiredRole); // rank-based check
}
```

## Admin Feature Slice

All admin platform data lives in `features/platform/`.

```
features/platform/
├── platform.types.ts          PlatformStatsResponse, PlatformSubscriptionResponse,
│                              PlatformReferralCodeResponse, IndustryReportSummaryResponse
├── platformApi.ts             RTK Query slice (reducerPath: "platformApi")
├── index.ts                   public exports
└── components/
    ├── AdminDashboardPage.tsx     home screen (KPI cards, at-risk widget, quick nav)
    ├── AdminStudioListPage.tsx    all studios + suspend/unsuspend
    ├── PlanManagementPage.tsx     plan CRUD (incl. AllowBrandingRemoval toggle)
    ├── SubscriptionOversightPage.tsx  all subscriptions + trial extension
    ├── PlatformReferralPage.tsx   all referral codes + deactivate
    └── IndustryReportsPage.tsx    monthly report links
```

`platformApi` tag types: `PlatformStats`, `PlatformSubscription`, `PlatformReferral`, `IndustryReport`.

Do NOT add admin platform queries to `billingApi` or `studiosApi` — keep them in `platformApi`.

---

## SignalR Hook

```typescript
// shared/hooks/useSignalR.ts
export function useSignalR(studioId: string) {
  const token = useAppSelector((s) => s.auth.token);
  const dispatch = useAppDispatch();

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/schedule", { accessTokenFactory: () => token ?? "" })
      .withAutomaticReconnect()
      .build();

    connection.on("AppointmentCreated", (appt) => {
      dispatch(appointmentsApi.util.invalidateTags(["Appointment"]));
    });

    connection.start().then(() => connection.invoke("JoinStudio", studioId));
    return () => { connection.stop(); };
  }, [studioId, token]);
}
```

---

## Payment UI Pattern

Client-facing deposit payments (Flow A) use `PaymentMethodSelector`
(`features/payments/components/PaymentMethodSelector.tsx`) — tabs for **Card** (labelled "New
card" when the client has saved cards), **Saved card** (only then) and **Cash**, plus the
gift-card redemption field. No PayPal, no wallet providers. Do not duplicate payment UI in other
components.

**Card tab:** POK's `GuestCheckoutForm` from `@nebula-ltd/pok-payments-js/react`, mounted with
the POK order id (the payment's `clientToken`) and `options.env` taken from
`GET /api/v1/payments/capabilities` → `pokEnvironment` — never guessed from the build mode. The
widget's `onSuccess` is UX only (ADR-0001): the page re-asks the API (`createDepositPayment` resume
/ `confirmCardPayment`), which re-reads the order from POK before anything is shown as authorised.
Deposits are authorised first (POK `autoCapture: false`; shown to owners as "On hold") and charged
on Capture deposit, Complete or No-show, and partly on a late cancellation. The standalone link
page `/pay/:paymentId` is `DepositCheckoutPage`. Verified against POK staging 2026-10-14
(`docs/payments/runbook-studio-currency-migration-2026-09-27.md`, "§3.1 result").

**Saved-card tab:** `AddCardForm`/tokenization — known broken, see
`docs/specs/pok-flow-a-go-live-blockers-2026-10-07.md` (C-9). Do not extend it until that is fixed.

**Cash tab:** informational panel; on confirm calls `declareCashDeposit` → `Payment { Status = CashPending }`.
If a card hold exists and is still unpaid, the API releases it at POK first.

**Environment variables (never hardcoded in source):**

```
VITE_CONTACT_EMAIL            — Platform contact email (SuspensionBanner, SubscribePage, BillingPage, CurrencySettingsCard)
VITE_STRIPE_PUBLISHABLE_KEY   — still passed as a build arg (docker-compose.yml, frontend/Dockerfile, cd.yml; placeholder
                                in frontend/.env.example) but read by no
                                frontend code: Flow B redirects to server-created Stripe Checkout and billing-portal
                                URLs. Vestigial; Flow A has no publishable key (POK's widget needs only the order id).
```

Both go in `.env.local` (gitignored). A `.env.example` with placeholder values is committed.
Do NOT add `VITE_PAYPAL_CLIENT_ID` — PayPal is not used.

**`CashDepositConfirmButton`** (`features/payments/components/CashDepositConfirmButton.tsx`)
is the owner/artist-facing button shown in the dashboard for `CashPending` payments.
Calls `confirmCashDeposit` mutation on approval.

---

## Currency (Studio Currency, 2026-09-27)

Every studio has its own `currency` (ISO 4217), exposed on `StudioResponse`/`PublicStudioResponse`
and on every money record's own response (`PaymentResponse`, `GiftCardResponse`,
`PackagePurchaseResponse`, `BoothRentChargeResponse`, `DesignCatalogItemResponse`).

- **All money goes through `formatCurrency`/`MoneyInput`/`currencyLabel`
  (`shared/utils/formatCurrency.ts`, `shared/components/ui/money-input.tsx`) — never hard-code a
  currency (`"EUR"`, `€`) or a locale (`"pt-PT"`) anywhere in a component.** `APP_LOCALE` in
  `formatCurrency.ts` is the one fixed exception, and it's fixed on purpose (see Decisions Log).
- Format a **record's own amount** (a payment, gift card, package purchase, booth-rent charge)
  with **that record's own `currency` field**, not the studio's current one — they can differ if
  the studio's currency changes after the record existed.
- Format a **price setting** (a service, deposit rule, promo code, package, artist hourly rate)
  with the studio's *current* currency, via the `useStudioCurrency()` hook
  (`shared/hooks/useStudioCurrency.ts`) for an authenticated owner/artist/client, or the
  `currency`/`countryCode` fields already present on a `PublicStudioResponse`/
  `PublicArtistResponse` prop for a guest/public page — never fetch the owner-only `studios/me`
  endpoint from a public component.
- A money **input** field uses `MoneyInput`, not a raw `<Input type="number">` — it renders the
  right currency adornment and `step` (1 for JPY, 0.01 for EUR/ALL, 0.001 for KWD) automatically.
  Pair it with `hasAtMostCurrencyMinorUnits`/`tooManyDecimalsMessage` (same file) for client-side
  decimal-precision validation matching the backend's own message.
- A currency **picker** (registration, Studio Settings) uses `CurrencySelect`
  (`shared/components/ui/currency-select.tsx`), which sources its option list from
  `shared/utils/currencies.ts` — never build a second country→currency or currency-list table on
  the frontend; the backend's `GET /public/countries/{code}/default-currency` endpoint is the only
  source of a country's default.
- Never render `<Select value={...} onValueChange={...}>` (Radix, incl. `CurrencySelect`'s own
  internals) with a field whose value starts **empty/falsy and later becomes non-empty** without
  guarding the `onValueChange` callback against an empty string first. Radix's hidden native-
  `<select>` autofill shim can fire a spurious `onValueChange("")` on that first uncontrolled→
  controlled transition, silently resetting the field — confirmed as a real bug in
  `RegisterStudioPage.tsx`'s currency picker, not just a test artifact. A country/currency field
  that's *never* empty at mount (has a real default from the first render) doesn't need the guard.

---

## Typography, radius and breakpoints (After Hours slice 5, 2026-11-18)

**Fonts.** The body family is **DM Sans** (`--font-sans`, `font-sans`); the TattooOS wordmark is
**Bodoni Moda 700** through its own token `--font-wordmark` (`font-wordmark`), whose one consumer is
`shared/components/Wordmark.tsx`. Use `<Wordmark />` wherever the product name is a brand mark. Never put
Bodoni in `--font-sans` and never use `font-wordmark` anywhere else: docs/03 says ornamental lettering is
for the wordmark only, and `shared/__tests__/typography.test.tsx` fails on a second consumer. Fraunces is
gone entirely.

- **Only the faces the scale names load:** DM Sans 400/500/600 and Bodoni Moda 700, as self-hosted woff2
  in `public/fonts/` with their OFL licence files beside them (docs/11; see `public/fonts/README.md`).
  There is no 700 DM Sans face: `font-bold` resolves to the 600 face. If the scale ever names 700, add
  the face deliberately. There are no italic faces, so `italic` renders a synthesised oblique.
- **No third-party font origin** (Google Fonts was removed), so a future Content-Security-Policy needs no
  `font-src` exception.
- `index.html` preloads only `dm-sans-400.woff2`.

**Radius.** Every corner is **3px**, and **the app conforms to the design package** (corrected 24 Nov).
`docs/03`'s only radius line is "Radius baseline: 3px". The four-role prototype CSS, which gives 3px only
to buttons and inputs and leaves panels square, is the package's visual reference, not its specification,
so its square panels are not a requirement and 3px everywhere is not a divergence. Phi left the scale as is
on 24 Nov; don't reopen `--radius*` without a new decision. In
Tailwind v4, `--radius` only reaches the bare `rounded` utility and the toast variable.
`rounded-sm/-md/-lg/-xl` read Tailwind's own `--radius-*` scale, so those are set to 3px as well.
`rounded-xs` stays 2px, as the inner corner of a control nested in a 3px box. `rounded-full` is a shape
(pills, avatars, dots) and is unchanged.

**Breakpoints: two sets, each with its own job.**

| Variant | Width | Job |
|---|---|---|
| `sm:` / `md:` / `lg:` | 640 / 768 / 1024 | **The shared shell** (sidebar, header, drawer), unchanged. The JS in `shouldOpenNavDrawerForTourStep.ts` and `OnboardingTour.tsx` hard-codes 640 and 1024 to match. |
| `page-sm:` / `page-md:` / `page-lg:` | 420 / 800 / 1100 | **Page content** from the After Hours four-role CSS. The prototype writes them as max-width queries, so `max-page-md:` (below 800) is the direct translation of "below 800px it stacks". |

docs/03 says to keep the existing shared shell and use the four-role values as content-driven page
breakpoints, so `lg` still means 1024. **The token JSON's 720/1050 are legacy metadata: never use them.**

**Which set new code uses:** `page-*` for any page-level layout band (when a page's columns stack, where a
page's grid changes). `sm:` / `md:` / `lg:` only for the shell, or inside a component that has its own
reason to differ from the page, and say that reason in a comment.

**Slice 6c moved the authenticated pages** (the Decisions Log has the table). The rule it applied: a page's
columns stack below 800 (`sm:grid-cols-N` → `page-md:grid-cols-N`, and title/actions rows likewise); a side
column joins at 1100 (`lg:` → `page-lg:`), so **a 1024–1099px window gets the stacked layout**; a small control
pair goes side by side at 420 (`page-sm:`). Deliberately left on the shell scale: page header and scope-bar
gutters (`px-4 sm:px-6`, which must line up with the shell header's own gutter), anything inside a fixed-width
card (the booking form's date/length split sits in a `max-w-md` card, so the viewport is the wrong measure),
the artist profile's six-tab strip (needs ~600px), the schedule toolbar's label width, sheet widths (the sheet
primitive's own), and component-internal layout (`AppointmentCard`, `DataTable`'s card/table switch,
`ConnectionRow`).

**Slice 6e moved the public product pages by the same rule** (artist portfolio, studio portfolio, Discover,
the portfolio feed), so a client sees the same column count on an artist's work signed in or out. The artist
page's fixed "Book with …" bar and the padding that clears it (`page-lg:hidden` / `pb-20 page-lg:pb-8`) move
**with** its side column: the bar shows exactly while the column holding the booking button is stacked, so the
three change together or not at all. Left on the shell scale there: the two image lightboxes' split (`md:`,
sized to the dialog's own `max-w-3xl` = 768px, not to the page), the horizontal scrollers' gutter bleed
(`-mx-4 px-4 sm:mx-0 sm:px-0`, a gutter), and Discover's location name beside "Near me" (shown whenever that
chip row has room, which it does from 640 in both search layouts). **The marketing pages stay out**
(pricing, features, use cases, legal): handoff pack `docs/01:24` reserves those.

**Target sizes: 44px and 46px are project targets, not WCAG.** docs/03 sets frequent controls at a
46px minimum height, with 44px as a comfortable-target project goal. WCAG 2.2 AA (2.5.8) is **24x24 CSS px
with spacing exceptions** (docs/08). Never describe 44 or 46 as a WCAG or AA requirement in a comment, PR
or test name. Check width and spacing too, not height alone.

---

## Component Rules

- One component per file. File name matches component name.
- No inline styles. Tailwind only.
- Use `shadcn/ui` primitives before writing a custom component.
- Never fetch data inside a component — use RTK Query hooks.
- Never access Redux state outside of components/hooks — no store imports in utilities.
- Export components as named exports, not default (easier refactoring).

### Component states (slice 6a)

The 8 states (default, hover, focus-visible, pressed/selected, disabled-with-explanation, loading, error, settled
success) live in the shared primitives, so use them rather than re-creating them:

- **A control that can't be used right now says why:** `<Button disabledReason="...">`. It stays focusable and the
  reason shows on hover and keyboard focus and is read as its description. A `title` is not a reason (no keyboard,
  no touch). If you can't give a reason, don't disable the control: leave it out, or let it refuse with a message.
- **A running action:** `<Button loading>`, not `disabled={isLoading}`. Disabling a focused button drops keyboard
  focus to the page; `loading` keeps it and refuses repeat clicks.
- **A field in error:** set `aria-invalid` on `Input`/`Textarea`/`SelectTrigger` and it looks invalid by itself.
- **Announcing a status:** `LiveAnnouncement` only, mounted before its text changes. Announce what the person caused,
  once, politely; never announce a ticking value on every tick (pass a message that changes only at thresholds).
  For a list's result count after a search or filter, use `useResultCountAnnouncement`.
- Selected state (`aria-pressed`/`aria-selected`) stays the caller's styling: several toggles switch variant on it.

---

## Testing "X never appears before Y"

A test that asserts something is never shown before something else (a first paint, a flash of the wrong state) can use neither `findBy*` nor a plain assertion straight after `render()`. Both see the screen after effects have run: `findBy*` retries until it matches, and RTL wraps `render()` in `act()`, which flushes effects before it returns. Either would pass on a page that does flash. Record the first commit instead, from a `useLayoutEffect` in a probe component mounted after the component under test, and assert on what it captured (see `FirstCommitProbe` in `features/payments/__tests__/PaymentResultPage.test.tsx`). Prove the probe works with a mutation check: make the page show X on its first render only, and check the test fails.

---

## TypeScript Rules

- `strict: true` in `tsconfig.json` — no exceptions.
- No `any`. Use `unknown` and narrow.
- All API response shapes typed in `shared/types/` or co-located in the feature.
- Const objects + type aliases for roles and status values — never raw strings in logic.
- Do NOT use TypeScript `enum` — the project has `erasableSyntaxOnly: true` in tsconfig which disallows enums.

```typescript
// shared/types/roles.ts
export const Role = {
  Client: "client",
  Artist: "artist",
  Owner:  "owner",
  Admin:  "admin",
} as const;

export type Role = typeof Role[keyof typeof Role];
```
