import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import { decodeToken } from "@/shared/utils/jwt";
import type { AuthPayload, Role, User } from "@/shared/types/roles";

const TOKEN_KEY         = "auth_token";
const REFRESH_TOKEN_KEY = "auth_refresh_token";

interface AuthState {
  user:                User | null;
  token:               string | null;
  refreshToken:        string | null;
  tenantId:            string | null;
  role:                Role | null;
  pendingReferralCode: string | null;
}

const EMPTY: AuthState = {
  user: null, token: null, refreshToken: null,
  tenantId: null, role: null, pendingReferralCode: null,
};

function loadInitialState(): AuthState {
  try {
    const token        = localStorage.getItem(TOKEN_KEY);
    const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY);
    if (!token) return EMPTY;

    const payload = decodeToken(token);
    if (payload.exp && Date.now() / 1000 > payload.exp) {
      localStorage.removeItem(TOKEN_KEY);
      localStorage.removeItem(REFRESH_TOKEN_KEY);
      return EMPTY;
    }

    return {
      user: payload.user, token, refreshToken,
      tenantId: payload.tenantId, role: payload.role,
      pendingReferralCode: null,
    };
  } catch {
    return EMPTY;
  }
}

const authSlice = createSlice({
  name: "auth",
  initialState: loadInitialState,
  reducers: {
    setCredentials: (state, { payload }: PayloadAction<AuthPayload>) => {
      state.user         = payload.user;
      state.token        = payload.token;
      state.refreshToken = payload.refreshToken ?? null;
      state.tenantId     = payload.tenantId;
      state.role         = payload.role;
      localStorage.setItem(TOKEN_KEY, payload.token);
      if (payload.refreshToken) {
        localStorage.setItem(REFRESH_TOKEN_KEY, payload.refreshToken);
      }
    },
    setPendingReferralCode: (state, { payload }: PayloadAction<string | null>) => {
      state.pendingReferralCode = payload;
    },
    logout: () => {
      localStorage.removeItem(TOKEN_KEY);
      localStorage.removeItem(REFRESH_TOKEN_KEY);
      return EMPTY;
    },
  },
});

export const { setCredentials, setPendingReferralCode, logout } = authSlice.actions;
export default authSlice.reducer;
