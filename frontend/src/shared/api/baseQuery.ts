import { fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { BaseQueryFn, FetchArgs, FetchBaseQueryError } from "@reduxjs/toolkit/query";
import type { RootState } from "@/app/store";
import {
  setReadOnlyError, setSessionExpired, setStudioSuspended, setPlanLimitError,
  setImpersonationScopeError, setImpersonationSessionExpired,
} from "@/features/ui/uiSlice";
import { setCredentials, logout, endImpersonation } from "@/features/auth/authSlice";
import { decodeToken } from "@/shared/utils/jwt";

const rawBaseQuery = fetchBaseQuery({
  baseUrl: "/api/v1/",
  prepareHeaders: (headers, { getState }) => {
    const { token, tenantId } = (getState() as RootState).auth;
    if (token)    headers.set("Authorization", `Bearer ${token}`);
    if (tenantId) headers.set("X-Tenant-Id", tenantId);
    return headers;
  },
});

// Simple async lock — only one refresh in-flight at a time.
let refreshLock: Promise<void> | null = null;

export const baseQuery: BaseQueryFn<string | FetchArgs, unknown, FetchBaseQueryError> =
  async (args, api, extraOptions) => {
    if (refreshLock) await refreshLock;

    let result = await rawBaseQuery(args, api, extraOptions);

    if (result.error?.status === 401) {
      if (!refreshLock) {
        let unlock!: () => void;
        refreshLock = new Promise((res) => { unlock = res; });

        try {
          const { refreshToken, impersonation } = (api.getState() as RootState).auth;

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
            }));
            result = await rawBaseQuery(args, api, extraOptions);
          } else {
            api.dispatch(logout());
            api.dispatch(setSessionExpired());
          }
        } catch {
          api.dispatch(logout());
          api.dispatch(setSessionExpired());
        } finally {
          refreshLock = null;
          unlock();
        }
      } else {
        await refreshLock;
        result = await rawBaseQuery(args, api, extraOptions);
        if (result.error?.status === 401) {
          api.dispatch(setSessionExpired());
        }
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
