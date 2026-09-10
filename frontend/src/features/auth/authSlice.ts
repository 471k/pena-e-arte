import { createSlice, type PayloadAction } from "@reduxjs/toolkit";
import { decodeToken } from "@/shared/utils/jwt";
import type { AuthPayload, Role, User } from "@/shared/types/roles";

const TOKEN_KEY         = "auth_token";
const REFRESH_TOKEN_KEY = "auth_refresh_token";

// Support Impersonation — kept in sessionStorage only (never localStorage), deliberately
// separate from the normal token slot above: starting a session stashes the real admin's
// own token/refresh token aside here so "End session" (or an automatic end on expiry) can
// restore it, without the admin ever having to log back in. See authSlice's
// startImpersonation/endImpersonation reducers and docs/claude/architecture.md Decisions
// Log — "Support Impersonation with Audit Trail".
const IMPERSONATION_STASH_KEY = "auth_impersonation_stash";
const IMPERSONATION_META_KEY  = "auth_impersonation_meta";

interface ImpersonationMeta {
  sessionId:  string;
  studioId:   string;
  studioName: string;
  expiresAt:  string;
}

interface ImpersonationStash {
  token:        string;
  refreshToken: string | null;
  remember:     boolean;
}

interface AuthState {
  user:                User | null;
  token:               string | null;
  refreshToken:        string | null;
  tenantId:            string | null;
  role:                Role | null;
  pendingReferralCode: string | null;
  impersonation:       ImpersonationMeta | null;
}

const EMPTY: AuthState = {
  user: null, token: null, refreshToken: null,
  tenantId: null, role: null, pendingReferralCode: null, impersonation: null,
};

function readImpersonationMeta(sessionId: string, tenantId: string | null, exp: number | undefined): ImpersonationMeta {
  try {
    const raw = sessionStorage.getItem(IMPERSONATION_META_KEY);
    if (raw) {
      const meta = JSON.parse(raw) as ImpersonationMeta;
      if (meta.sessionId === sessionId) return meta;
    }
  } catch {
    // fall through to the defensive fallback below
  }
  // Meta missing/mismatched (e.g. a different browser tab) — still surface the banner
  // rather than silently rendering a normal admin view while holding an impersonation
  // token. Studio name is unknown in this fallback; the banner shows a generic label.
  return {
    sessionId,
    studioId: tenantId ?? "",
    studioName: "this studio",
    expiresAt: exp ? new Date(exp * 1000).toISOString() : new Date().toISOString(),
  };
}

function loadInitialState(): AuthState {
  try {
    const token        = localStorage.getItem(TOKEN_KEY) ?? sessionStorage.getItem(TOKEN_KEY);
    const refreshToken = localStorage.getItem(REFRESH_TOKEN_KEY) ?? sessionStorage.getItem(REFRESH_TOKEN_KEY);
    if (!token) return EMPTY;

    const payload = decodeToken(token);
    if (payload.exp && Date.now() / 1000 > payload.exp) {
      localStorage.removeItem(TOKEN_KEY);
      localStorage.removeItem(REFRESH_TOKEN_KEY);
      sessionStorage.removeItem(TOKEN_KEY);
      sessionStorage.removeItem(REFRESH_TOKEN_KEY);
      return EMPTY;
    }

    const impersonation = payload.impersonationSessionId
      ? readImpersonationMeta(payload.impersonationSessionId, payload.tenantId, payload.exp)
      : null;

    return {
      user: payload.user, token, refreshToken,
      tenantId: payload.tenantId, role: payload.role,
      pendingReferralCode: null,
      impersonation,
    };
  } catch {
    return EMPTY;
  }
}

const authSlice = createSlice({
  name: "auth",
  initialState: loadInitialState,
  reducers: {
    setCredentials: (state, { payload }: PayloadAction<AuthPayload & { remember?: boolean }>) => {
      state.user         = payload.user;
      state.token        = payload.token;
      state.refreshToken = payload.refreshToken ?? null;
      state.tenantId     = payload.tenantId;
      state.role         = payload.role;

      const storage = payload.remember !== false ? localStorage : sessionStorage;
      storage.setItem(TOKEN_KEY, payload.token);
      if (payload.refreshToken) {
        storage.setItem(REFRESH_TOKEN_KEY, payload.refreshToken);
      }
    },
    setPendingReferralCode: (state, { payload }: PayloadAction<string | null>) => {
      state.pendingReferralCode = payload;
    },
    logout: () => {
      localStorage.removeItem(TOKEN_KEY);
      localStorage.removeItem(REFRESH_TOKEN_KEY);
      sessionStorage.removeItem(TOKEN_KEY);
      sessionStorage.removeItem(REFRESH_TOKEN_KEY);
      try {
        sessionStorage.removeItem(IMPERSONATION_STASH_KEY);
        sessionStorage.removeItem(IMPERSONATION_META_KEY);
      } catch {
        // sessionStorage unavailable — nothing to clean up
      }
      return EMPTY;
    },
    // Swaps the admin's own session token for a short-lived impersonation token, stashing
    // the real token aside (sessionStorage only) so endImpersonation can restore it.
    startImpersonation: (
      state,
      { payload }: PayloadAction<{ accessToken: string; sessionId: string; studioId: string; studioName: string; expiresAt: string }>,
    ) => {
      const remember = localStorage.getItem(TOKEN_KEY) !== null;
      try {
        const stash: ImpersonationStash = {
          token: state.token ?? "",
          refreshToken: state.refreshToken,
          remember,
        };
        sessionStorage.setItem(IMPERSONATION_STASH_KEY, JSON.stringify(stash));
        const meta: ImpersonationMeta = {
          sessionId: payload.sessionId, studioId: payload.studioId,
          studioName: payload.studioName, expiresAt: payload.expiresAt,
        };
        sessionStorage.setItem(IMPERSONATION_META_KEY, JSON.stringify(meta));
      } catch {
        // sessionStorage unavailable — the session still works for this page load, but
        // "End session" will fall back to a full logout (see endImpersonation below).
      }

      const decoded = decodeToken(payload.accessToken);
      state.user         = decoded.user;
      state.token         = payload.accessToken;
      state.refreshToken  = null; // impersonation is meant to hard-expire, not renew
      state.tenantId       = payload.studioId;
      state.role          = decoded.role;
      state.impersonation = {
        sessionId: payload.sessionId, studioId: payload.studioId,
        studioName: payload.studioName, expiresAt: payload.expiresAt,
      };

      localStorage.removeItem(TOKEN_KEY);
      localStorage.removeItem(REFRESH_TOKEN_KEY);
      sessionStorage.setItem(TOKEN_KEY, payload.accessToken);
      sessionStorage.removeItem(REFRESH_TOKEN_KEY);
    },
    // Restores the real admin's own token from the stash. Falls back to a full logout if
    // the stash is missing (e.g. a different tab, or sessionStorage was cleared) — safer
    // than leaving the admin on a token nothing can account for.
    endImpersonation: (state) => {
      let stash: ImpersonationStash | null;
      try {
        const raw = sessionStorage.getItem(IMPERSONATION_STASH_KEY);
        stash = raw ? (JSON.parse(raw) as ImpersonationStash) : null;
      } catch {
        stash = null;
      }

      if (!stash || !stash.token) {
        localStorage.removeItem(TOKEN_KEY);
        localStorage.removeItem(REFRESH_TOKEN_KEY);
        sessionStorage.removeItem(TOKEN_KEY);
        sessionStorage.removeItem(REFRESH_TOKEN_KEY);
        return EMPTY;
      }

      const decoded = decodeToken(stash.token);
      const storage = stash.remember ? localStorage : sessionStorage;

      sessionStorage.removeItem(TOKEN_KEY);
      sessionStorage.removeItem(REFRESH_TOKEN_KEY);
      storage.setItem(TOKEN_KEY, stash.token);
      if (stash.refreshToken) storage.setItem(REFRESH_TOKEN_KEY, stash.refreshToken);

      try {
        sessionStorage.removeItem(IMPERSONATION_STASH_KEY);
        sessionStorage.removeItem(IMPERSONATION_META_KEY);
      } catch {
        // ignore
      }

      state.user         = decoded.user;
      state.token         = stash.token;
      state.refreshToken  = stash.refreshToken;
      state.tenantId       = decoded.tenantId;
      state.role          = decoded.role;
      state.impersonation = null;
    },
  },
});

export const {
  setCredentials, setPendingReferralCode, logout,
  startImpersonation, endImpersonation,
} = authSlice.actions;
export default authSlice.reducer;
