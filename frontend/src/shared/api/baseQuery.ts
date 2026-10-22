import { fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { BaseQueryApi, BaseQueryFn, FetchArgs, FetchBaseQueryError } from "@reduxjs/toolkit/query";
import type { RootState } from "@/app/store";
import {
  setReadOnlyError, setSessionExpired, setStudioSuspended, setPlanLimitError,
  setImpersonationScopeError, setImpersonationSessionExpired,
} from "@/features/ui/uiSlice";
import { setCredentials, logout, endImpersonation, readStoredSession } from "@/features/auth/authSlice";
import { decodeToken, isTokenExpired } from "@/shared/utils/jwt";

const rawBaseQuery = fetchBaseQuery({
  baseUrl: "/api/v1/",
  prepareHeaders: (headers, { getState }) => {
    const { token, tenantId } = (getState() as RootState).auth;
    if (token)    headers.set("Authorization", `Bearer ${token}`);
    if (tenantId) headers.set("X-Tenant-Id", tenantId);
    return headers;
  },
});

// Renew a little before the access token's stated expiry, so a request isn't sent with a token
// that runs out on the way.
const EXPIRY_MARGIN_SECONDS = 30;

const isAccessTokenExpired = (token: string) => isTokenExpired(token, EXPIRY_MARGIN_SECONDS);

// The backend keeps ONE refresh token per user and replaces it on every renewal, so a second tab
// renewing with the old one is refused. Before renewing, and again if a renewal is refused, take
// the session another tab has already renewed (it is in the shared storage) instead of signing out.
function adoptStoredSession(api: BaseQueryApi, refreshTokenUsed: string | null): boolean {
  const stored = readStoredSession();
  if (!stored.token || !stored.refreshToken || stored.refreshToken === refreshTokenUsed) return false;
  if (isAccessTokenExpired(stored.token)) return false;

  const decoded = decodeToken(stored.token);
  api.dispatch(setCredentials({
    token:        stored.token,
    refreshToken: stored.refreshToken,
    tenantId:     decoded.tenantId,
    role:         decoded.role,
    user:         decoded.user,
    remember:     stored.remember,
  }));
  return true;
}

// Tabs renew one at a time (Web Locks, shared by every tab of this site), so the second tab finds
// the first one's new session in storage instead of renewing with the token it just replaced.
// Without Web Locks (old browsers, tests) the storage checks around the renewal still apply.
function withCrossTabLock<T>(fn: () => Promise<T>): Promise<T> {
  const locks = typeof navigator !== "undefined" ? navigator.locks : undefined;
  return locks ? locks.request("tattooos-session-renewal", fn) : fn();
}

// Simple async lock — only one refresh in-flight at a time.
let refreshLock: Promise<void> | null = null;

type Renewal = "renewed" | "refused" | "unavailable";

// Renews the session once, however many requests need it at the same moment. Only a refresh token
// the server refuses ends the session (signed out, sent to the login page). A network error, a
// server error or a renewal cut short (the tab closing or reloading) keeps it: signing out would
// clear the storage every other tab shares, and the next request simply tries again.
async function renewSession(
  api: BaseQueryApi,
  extraOptions: Parameters<typeof rawBaseQuery>[2],
): Promise<boolean> {
  if (refreshLock) {
    await refreshLock;
    return (api.getState() as RootState).auth.token !== null;
  }

  let unlock!: () => void;
  refreshLock = new Promise((res) => { unlock = res; });

  try {
    const outcome = await withCrossTabLock(async (): Promise<Renewal> => {
      const { refreshToken } = (api.getState() as RootState).auth;
      if (adoptStoredSession(api, refreshToken)) return "renewed";

      const refreshResult = await rawBaseQuery(
        { url: "auth/refresh", method: "POST", body: { refreshToken } },
        api,
        extraOptions,
      );

      if (refreshResult.data) {
        const data = refreshResult.data as { accessToken: string; refreshToken: string; tokenType: string };
        const decoded = decodeToken(data.accessToken);
        api.dispatch(setCredentials({
          token:        data.accessToken,
          refreshToken: data.refreshToken,
          tenantId:     decoded.tenantId,
          role:         decoded.role,
          user:         decoded.user,
          // Keep the tokens where sign-in put them: "remember me" off means sessionStorage.
          remember:     readStoredSession().remember,
        }));
        return "renewed";
      }

      if (adoptStoredSession(api, refreshToken)) return "renewed";
      // The API refuses an unknown, replaced or expired refresh token with 422 (400 if it's
      // empty). Timeouts (408) and rate limits (429) are worth another try.
      const status = refreshResult.error?.status;
      const refused = typeof status === "number" && status >= 400 && status < 500 && status !== 408 && status !== 429;
      return refused ? "refused" : "unavailable";
    });
    if (outcome === "refused") {
      api.dispatch(logout());
      api.dispatch(setSessionExpired());
    }
    return outcome === "renewed";
  } catch {
    return false;
  } finally {
    refreshLock = null;
    unlock();
  }
}

export const baseQuery: BaseQueryFn<string | FetchArgs, unknown, FetchBaseQueryError> =
  async (args, api, extraOptions) => {
    if (refreshLock) await refreshLock;

    // An access token that has already expired (the app reopened after a break, or a link opened
    // from an email) is renewed before the request, instead of every request on the page failing
    // with 401 at once.
    const before = (api.getState() as RootState).auth;
    if (before.token && before.refreshToken && isAccessTokenExpired(before.token)) {
      await renewSession(api, extraOptions);
    }

    const tokenSent = (api.getState() as RootState).auth.token;
    let result = await rawBaseQuery(args, api, extraOptions);

    if (result.error?.status === 401) {
      const { token, refreshToken, impersonation } = (api.getState() as RootState).auth;

      // Another request renewed the session while this one was on its way: just send it again.
      if (token !== null && token !== tokenSent) {
        result = await rawBaseQuery(args, api, extraOptions);
        if (result.error?.status === 401) api.dispatch(setSessionExpired());
        return result;
      }

      if (!refreshToken) {
        // An impersonation token has no refresh token by design (see authSlice's
        // startImpersonation) — a 401 here means the session's own token expired.
        // Restore the admin's own stashed token instead of a full logout, mirroring
        // what "End session" does, rather than forcing the admin to log back in.
        if (impersonation) {
          api.dispatch(endImpersonation());
          api.dispatch(setImpersonationSessionExpired());
          return result;
        }
        api.dispatch(logout());
        api.dispatch(setSessionExpired());
        return result;
      }

      if (await renewSession(api, extraOptions)) {
        result = await rawBaseQuery(args, api, extraOptions);
        if (result.error?.status === 401) api.dispatch(setSessionExpired());
      }
      return result;
    }

    if (result.error?.status === 402) {
      const data = result.error.data as { message?: string } | undefined;
      const message = data?.message ?? "Your studio is in read-only mode.";
      api.dispatch(setReadOnlyError(message));
    }

    if (result.error?.status === 403) {
      const data = result.error.data as { code?: string; message?: string } | undefined;
      if (data?.code === "STUDIO_SUSPENDED") {
        api.dispatch(setStudioSuspended());
      } else if (data?.code === "PLAN_LIMIT_EXCEEDED") {
        api.dispatch(setPlanLimitError(data.message ?? "This studio's plan limit was reached."));
      } else if (data?.code === "IMPERSONATION_SCOPE_DENIED") {
        api.dispatch(setImpersonationScopeError(
          data.message ?? "This action is not available while impersonating a studio.",
        ));
      }
    }

    return result;
  };
