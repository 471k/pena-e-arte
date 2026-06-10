import { createSlice, type PayloadAction } from "@reduxjs/toolkit";

interface UiState {
  readOnlyError: string | null;
  sessionExpired: boolean;
}

const uiSlice = createSlice({
  name: "ui",
  initialState: { readOnlyError: null, sessionExpired: false } as UiState,
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
  },
});

export const { setReadOnlyError, clearReadOnlyError, setSessionExpired, clearSessionExpired } = uiSlice.actions;
export default uiSlice.reducer;
