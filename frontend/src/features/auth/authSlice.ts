import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import type { AuthPayload, Role, User } from "@/shared/types/roles";

interface AuthState {
  user: User | null;
  token: string | null;
  tenantId: string | null;
  role: Role | null;
}

const initialState: AuthState = {
  user: null,
  token: null,
  tenantId: null,
  role: null,
};

const authSlice = createSlice({
  name: "auth",
  initialState,
  reducers: {
    setCredentials: (state, { payload }: PayloadAction<AuthPayload>) => {
      state.user = payload.user;
      state.token = payload.token;
      state.tenantId = payload.tenantId;
      state.role = payload.role;
    },
    logout: () => initialState,
  },
});

export const { setCredentials, logout } = authSlice.actions;
export default authSlice.reducer;
