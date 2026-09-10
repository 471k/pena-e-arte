import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import { logout, endImpersonation } from "@/features/auth/authSlice";

interface UiState {
  readOnlyError:   string | null;
  sessionExpired:  boolean;
  studioSuspended: boolean;
  planLimitError:  string | null;
  impersonationScopeError:     string | null;
  impersonationSessionExpired: boolean;
}

const uiSlice = createSlice({
  name: "ui",
  initialState: {
    readOnlyError:   null,
    sessionExpired:  false,
    studioSuspended: false,
    planLimitError:  null,
    impersonationScopeError:     null,
    impersonationSessionExpired: false,
  } as UiState,
  reducers: {
    setReadOnlyError: (state, { payload }: PayloadAction<string>) => {
      state.readOnlyError = payload;
    },
    clearReadOnlyError: (state) => {
      state.readOnlyError = null;
    },
    setSessionExpired: (state) => {
      state.sessionExpired = true;
    },
    clearSessionExpired: (state) => {
      state.sessionExpired = false;
    },
    setStudioSuspended: (state) => {
      state.studioSuspended = true;
    },
    clearStudioSuspended: (state) => {
      state.studioSuspended = false;
    },
    setPlanLimitError: (state, { payload }: PayloadAction<string>) => {
      state.planLimitError = payload;
    },
    clearPlanLimitError: (state) => {
      state.planLimitError = null;
    },
    setImpersonationScopeError: (state, { payload }: PayloadAction<string>) => {
      state.impersonationScopeError = payload;
    },
    clearImpersonationScopeError: (state) => {
      state.impersonationScopeError = null;
    },
    // Set when an impersonation session's own token is rejected server-side (ended or
    // past its hard expiry) — AppRoot watches this to restore the admin's own token and
    // navigate them out, mirroring the sessionExpired pattern above.
    setImpersonationSessionExpired: (state) => {
      state.impersonationSessionExpired = true;
    },
    clearImpersonationSessionExpired: (state) => {
      state.impersonationSessionExpired = false;
    },
  },
  extraReducers: (builder) => {
    builder.addCase(logout, (state) => {
      state.studioSuspended = false;
      state.readOnlyError   = null;
      state.sessionExpired  = false;
      state.planLimitError  = null;
      state.impersonationScopeError     = null;
      state.impersonationSessionExpired = false;
    });
    builder.addCase(endImpersonation, (state) => {
      state.impersonationScopeError     = null;
      state.impersonationSessionExpired = false;
    });
  },
});

export const {
  setReadOnlyError, clearReadOnlyError,
  setSessionExpired, clearSessionExpired,
  setStudioSuspended, clearStudioSuspended,
  setPlanLimitError, clearPlanLimitError,
  setImpersonationScopeError, clearImpersonationScopeError,
  setImpersonationSessionExpired, clearImpersonationSessionExpired,
} = uiSlice.actions;

export default uiSlice.reducer;
