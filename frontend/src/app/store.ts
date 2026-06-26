import { configureStore, combineReducers } from "@reduxjs/toolkit";
import type { UnknownAction } from "@reduxjs/toolkit";
import { authApi } from "@/features/auth/authApi";
import authReducer, { logout } from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import notificationsReducer from "@/features/notifications/notificationsSlice";
import { studiosApi } from "@/features/studios/studiosApi";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { artistsApi } from "@/features/artists/artistsApi";
import { clientsApi } from "@/features/clients/clientsApi";
import { designsApi } from "@/features/designs/designsApi";
import { filesApi } from "@/shared/api/filesApi";
import { billingApi } from "@/features/billing/billingApi";
import { intakeFormsApi } from "@/features/forms/intakeFormsApi";
import { consentFormsApi } from "@/features/forms/consentFormsApi";
import { depositRulesApi } from "@/features/deposit-rules/depositRulesApi";
import { notificationsApi } from "@/features/notifications/notificationsApi";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { publicApi }   from "@/features/public";
import { platformApi } from "@/features/platform/platformApi";

const appReducer = combineReducers({
  auth:          authReducer,
  ui:            uiReducer,
  notifications: notificationsReducer,
  [authApi.reducerPath]:           authApi.reducer,
  [studiosApi.reducerPath]:        studiosApi.reducer,
  [appointmentsApi.reducerPath]:   appointmentsApi.reducer,
  [artistsApi.reducerPath]:        artistsApi.reducer,
  [clientsApi.reducerPath]:        clientsApi.reducer,
  [designsApi.reducerPath]:        designsApi.reducer,
  [filesApi.reducerPath]:          filesApi.reducer,
  [billingApi.reducerPath]:        billingApi.reducer,
  [intakeFormsApi.reducerPath]:    intakeFormsApi.reducer,
  [consentFormsApi.reducerPath]:   consentFormsApi.reducer,
  [depositRulesApi.reducerPath]:   depositRulesApi.reducer,
  [notificationsApi.reducerPath]:  notificationsApi.reducer,
  [paymentsApi.reducerPath]:       paymentsApi.reducer,
  [publicApi.reducerPath]:         publicApi.reducer,
  [platformApi.reducerPath]:       platformApi.reducer,
});

type AppState = ReturnType<typeof appReducer>;

// Passing undefined to every slice on logout resets all RTK Query caches,
// preventing stale data from one user's session leaking into the next.
function rootReducer(state: AppState | undefined, action: UnknownAction): AppState {
  return appReducer(action.type === logout.type ? undefined : state, action);
}

export const store = configureStore({
  reducer: rootReducer,
  middleware: (getDefault) =>
    getDefault().concat(
      authApi.middleware,
      studiosApi.middleware,
      appointmentsApi.middleware,
      artistsApi.middleware,
      clientsApi.middleware,
      designsApi.middleware,
      filesApi.middleware,
      billingApi.middleware,
      intakeFormsApi.middleware,
      consentFormsApi.middleware,
      depositRulesApi.middleware,
      notificationsApi.middleware,
      paymentsApi.middleware,
      publicApi.middleware,
      platformApi.middleware,
    ),
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
