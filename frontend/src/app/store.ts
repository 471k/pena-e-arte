import { configureStore } from "@reduxjs/toolkit";
import { authApi } from "@/features/auth/authApi";
import authReducer from "@/features/auth/authSlice";
import { studiosApi } from "@/features/studios/studiosApi";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { artistsApi } from "@/features/artists/artistsApi";
import { clientsApi } from "@/features/clients/clientsApi";
import { designsApi } from "@/features/designs/designsApi";
import { filesApi } from "@/shared/api/filesApi";
import { intakeFormsApi } from "@/features/forms/intakeFormsApi";
import { consentFormsApi } from "@/features/forms/consentFormsApi";
import { depositRulesApi } from "@/features/deposit-rules/depositRulesApi";

export const store = configureStore({
  reducer: {
    auth: authReducer,
    [authApi.reducerPath]:          authApi.reducer,
    [studiosApi.reducerPath]:       studiosApi.reducer,
    [appointmentsApi.reducerPath]:  appointmentsApi.reducer,
    [artistsApi.reducerPath]:       artistsApi.reducer,
    [clientsApi.reducerPath]:       clientsApi.reducer,
    [designsApi.reducerPath]:       designsApi.reducer,
    [filesApi.reducerPath]:         filesApi.reducer,
    [intakeFormsApi.reducerPath]:   intakeFormsApi.reducer,
    [consentFormsApi.reducerPath]:  consentFormsApi.reducer,
    [depositRulesApi.reducerPath]:  depositRulesApi.reducer,
  },
  middleware: (getDefault) =>
    getDefault().concat(
      authApi.middleware,
      studiosApi.middleware,
      appointmentsApi.middleware,
      artistsApi.middleware,
      clientsApi.middleware,
      designsApi.middleware,
      filesApi.middleware,
      intakeFormsApi.middleware,
      consentFormsApi.middleware,
      depositRulesApi.middleware,
    ),
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
