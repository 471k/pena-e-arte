import { configureStore, combineReducers } from "@reduxjs/toolkit";
import type { UnknownAction } from "@reduxjs/toolkit";
import { authApi } from "@/features/auth/authApi";
import authReducer, { logout, setCredentials } from "@/features/auth/authSlice";
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
import { publicApi }          from "@/features/public";
import { savedImagesApi }     from "@/features/public/savedImagesApi";
import { platformApi } from "@/features/platform/platformApi";
import { feedbackApi } from "@/features/feedback/feedbackApi";
import { reviewsApi } from "@/features/reviews/reviewsApi";
import { helpApi } from "@/features/help/helpApi";

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
  [savedImagesApi.reducerPath]:    savedImagesApi.reducer,
  [platformApi.reducerPath]:       platformApi.reducer,
  [feedbackApi.reducerPath]:       feedbackApi.reducer,
  [reviewsApi.reducerPath]:        reviewsApi.reducer,
  [helpApi.reducerPath]:           helpApi.reducer,
});

type AppState = ReturnType<typeof appReducer>;

// Passing undefined to every slice on logout resets all RTK Query caches,
// preventing stale data from one user's session leaking into the next.
function rootReducer(state: AppState | undefined, action: UnknownAction): AppState {
  if (action.type === logout.type) {
    return appReducer(undefined, action);
  }

  const prevTenantId = state?.auth.tenantId;
  const nextState     = appReducer(state, action);

  // A multi-studio client can switch their active studio (or log into a
  // different account) without a full page reload. None of the tenant-scoped
  // RTK Query cache keys include tenantId, so every cached response (artists,
  // clients, deposit rules, etc.) is now stale for the new tenant — reset
  // everything except auth/ui/notifications and publicApi (public data is
  // cache-keyed by slug already, not tenant-scoped — resetting it mid-switch
  // just forces a refetch loop against the in-flight studio lookup).
  // Deliberately NOT gated on prevTenantId being truthy: a studio-less client
  // (registered with no studio) can fetch tenant-scoped data against a null
  // tenant BEFORE ever switching (e.g. visiting /book directly), caching an
  // empty/wrong result under a tenant-agnostic cache key — that must also be
  // invalidated the moment they gain their first real tenantId.
  if (
    action.type === setCredentials.type &&
    nextState.auth.tenantId &&
    prevTenantId !== nextState.auth.tenantId
  ) {
    const freshState = appReducer(undefined, action);
    return {
      ...freshState,
      auth:          nextState.auth,
      ui:            nextState.ui,
      notifications: nextState.notifications,
      publicApi:     nextState.publicApi,
    };
  }

  return nextState;
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
      savedImagesApi.middleware,
      platformApi.middleware,
      feedbackApi.middleware,
      reviewsApi.middleware,
      helpApi.middleware,
    ),
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
