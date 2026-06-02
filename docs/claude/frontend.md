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
    └── IssuerLayout.tsx
```

---

## Redux Store Structure

```typescript
// app/store.ts
export const store = configureStore({
  reducer: {
    auth:          authReducer,
    ui:            uiReducer,
    notifications: notificationReducer,
    // RTK Query reducers
    [appointmentsApi.reducerPath]: appointmentsApi.reducer,
    [clientsApi.reducerPath]:      clientsApi.reducer,
    [billingApi.reducerPath]:      billingApi.reducer,
    [studiosApi.reducerPath]:      studiosApi.reducer,
    [designsApi.reducerPath]:      designsApi.reducer,
  },
  middleware: (getDefault) =>
    getDefault()
      .concat(appointmentsApi.middleware)
      .concat(clientsApi.middleware)
      .concat(billingApi.middleware)
      .concat(studiosApi.middleware)
      .concat(designsApi.middleware),
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
    element: <RoleGuard allowedRoles={["client", "artist", "owner", "issuer"]} />,
    children: [
      { path: "book",      element: <ClientLayout />,  ... },
      { path: "schedule",  element: <ArtistLayout />,  ... },
      { path: "dashboard", element: <OwnerLayout />,   ... },
      { path: "platform",  element: <IssuerLayout />,  ... },
    ],
  },
  { path: "/login", element: <LoginPage /> },
]);

// shared/hooks/usePermission.ts — use this for conditional UI rendering
export function usePermission(requiredRole: Role): boolean {
  const role = useAppSelector((s) => s.auth.role);
  return hasPermission(role, requiredRole); // rank-based check
}
```

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
- Enums for roles and status values — never raw strings in logic.

```typescript
// shared/types/roles.ts
export enum Role {
  Client = "client",
  Artist = "artist",
  Owner  = "owner",
  Issuer = "issuer",
}
```
