import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import { decodeToken } from "@/shared/utils/jwt";
import type { AuthPayload, Role, User } from "@/shared/types/roles";

const TOKEN_KEY = "auth_token";

interface AuthState {
  user: User | null;
  token: string | null;
  tenantId: string | null;
  role: Role | null;
}

function loadInitialState(): AuthState {
  try {
    const token = localStorage.getItem(TOKEN_KEY);
    if (!token) return { user: null, token: null, tenantId: null, role: null };

    const payload = decodeToken(token);
    // Discard expired tokens (exp is in seconds)
    const exp = (JSON.parse(atob(token.split(".")[1])) as { exp?: number }).exp;
    if (exp && Date.now() / 1000 > exp) {
      localStorage.removeItem(TOKEN_KEY);
      return { user: null, token: null, tenantId: null, role: null };
    }

    return { user: payload.user, token, tenantId: payload.tenantId, role: payload.role };
  } catch {
    return { user: null, token: null, tenantId: null, role: null };
  }
}

const authSlice = createSlice({
  name: "auth",
  initialState: loadInitialState,
  reducers: {
    setCredentials: (state, { payload }: PayloadAction<AuthPayload>) => {
      state.user     = payload.user;
      state.token    = payload.token;
      state.tenantId = payload.tenantId;
      state.role     = payload.role;
      localStorage.setItem(TOKEN_KEY, payload.token);
    },
    logout: () => {
      localStorage.removeItem(TOKEN_KEY);
      return { user: null, token: null, tenantId: null, role: null };
    },
  },
});

export const { setCredentials, logout } = authSlice.actions;
export default authSlice.reducer;
