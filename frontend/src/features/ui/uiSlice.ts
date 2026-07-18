import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import { logout } from "@/features/auth/authSlice";

interface UiState {
  readOnlyError:   string | null;
  sessionExpired:  boolean;
  studioSuspended: boolean;
  planLimitError:  string | null;
}

const uiSlice = createSlice({
  name: "ui",
  initialState: {
    readOnlyError:   null,
    sessionExpired:  false,
    studioSuspended: false,
    planLimitError:  null,
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
  },
  extraReducers: (builder) => {
    builder.addCase(logout, (state) => {
      state.studioSuspended = false;
      state.readOnlyError   = null;
      state.sessionExpired  = false;
      state.planLimitError  = null;
    });
  },
});

export const {
  setReadOnlyError, clearReadOnlyError,
  setSessionExpired, clearSessionExpired,
  setStudioSuspended, clearStudioSuspended,
  setPlanLimitError, clearPlanLimitError,
} = uiSlice.actions;

export default uiSlice.reducer;
