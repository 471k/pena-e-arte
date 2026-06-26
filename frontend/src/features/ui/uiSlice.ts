import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import { logout } from "@/features/auth/authSlice";

interface UiState {
  readOnlyError:   string | null;
  sessionExpired:  boolean;
  studioSuspended: boolean;
}

const uiSlice = createSlice({
  name: "ui",
  initialState: { readOnlyError: null, sessionExpired: false, studioSuspended: false } as UiState,
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
  },
  extraReducers: (builder) => {
    builder.addCase(logout, (state) => {
      state.studioSuspended = false;
      state.readOnlyError   = null;
      state.sessionExpired  = false;
    });
  },
});

export const {
  setReadOnlyError, clearReadOnlyError,
  setSessionExpired, clearSessionExpired,
  setStudioSuspended, clearStudioSuspended,
} = uiSlice.actions;

export default uiSlice.reducer;
