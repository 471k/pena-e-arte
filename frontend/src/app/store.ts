import { configureStore } from "@reduxjs/toolkit";
import { authApi } from "@/features/auth/authApi";
import authReducer from "@/features/auth/authSlice";
import { studiosApi } from "@/features/studios/studiosApi";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";

export const store = configureStore({
  reducer: {
    auth: authReducer,
    [authApi.reducerPath]:         authApi.reducer,
    [studiosApi.reducerPath]:      studiosApi.reducer,
    [appointmentsApi.reducerPath]: appointmentsApi.reducer,
  },
  middleware: (getDefault) =>
    getDefault().concat(authApi.middleware, studiosApi.middleware, appointmentsApi.middleware),
});

export type RootState = ReturnType<typeof store.getState>;
export type AppDispatch = typeof store.dispatch;
