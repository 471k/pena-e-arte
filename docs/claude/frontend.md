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

Client-facing payments use a two-tab selector: Card (Stripe) and Cash.
No PayPal. No wallet providers. No `PayPalScriptProvider` at the app root.

```tsx
// main.tsx — NO PayPal provider. Stripe Elements is initialised per-payment only.
// Stripe Elements needs the clientSecret from the server, so it is created per-component:
<Elements stripe={stripePromise} options={{ clientSecret }}>
  <PaymentElement />
</Elements>
```

**Environment variables (never hardcoded in source):**

```
VITE_STRIPE_PUBLISHABLE_KEY   — Stripe publishable key (pk_test_... or pk_live_...)
VITE_CONTACT_EMAIL            — Platform contact email shown on cash subscription info
```

Both go in `.env.local` (gitignored). A `.env.example` with placeholder values is committed.
Do NOT add `VITE_PAYPAL_CLIENT_ID` — PayPal is not used.

**`PaymentMethodSelector`** (`features/payments/components/PaymentMethodSelector.tsx`)
is the single component that renders Card and Cash tabs. Use it wherever a client needs
to pay a deposit. Do not duplicate payment UI in other components.

**Card tab:** Stripe `PaymentElement` (handles cards, Apple Pay, Google Pay automatically).
Uses manual capture — deposit is held then captured when the session is complete.

**Cash tab:** Informational panel explaining the client will pay at the studio.
On confirm, calls `declareCashDeposit` mutation → creates `Payment { Status = CashPending }`.
No Stripe call.

**`CashDepositConfirmButton`** (`features/payments/components/CashDepositConfirmButton.tsx`)
is the owner/artist-facing button shown in the dashboard for `CashPending` payments.
Calls `confirmCashDeposit` mutation on approval.

---

## Component Rules

- One component per file. File name matches component name.
- No inline styles. Tailwind only.
- Use `shadcn/ui` primitives before writing a custom component.
- Never fetch data inside a component — use RTK Query hooks.
- Never access Redux state outside of components/hooks — no store imports in utilities.
- Export components as named exports, not default (easier refactoring).

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
