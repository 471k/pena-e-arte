import { fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { BaseQueryFn, FetchArgs, FetchBaseQueryError } from "@reduxjs/toolkit/query";
import type { RootState } from "@/app/store";
import { setReadOnlyError, setSessionExpired, setStudioSuspended } from "@/features/ui/uiSlice";
import { setCredentials, logout } from "@/features/auth/authSlice";
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
          const { refreshToken } = (api.getState() as RootState).auth;

          if (!refreshToken) {
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
      const data = result.error.data as { code?: string } | undefined;
      if (data?.code === "STUDIO_SUSPENDED") {
        api.dispatch(setStudioSuspended());
      }
    }

    return result;
  };
