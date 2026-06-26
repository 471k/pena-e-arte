import { fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { BaseQueryFn, FetchArgs, FetchBaseQueryError } from "@reduxjs/toolkit/query";
import type { RootState } from "@/app/store";
import { setReadOnlyError, setSessionExpired, setStudioSuspended } from "@/features/ui/uiSlice";

const rawBaseQuery = fetchBaseQuery({
  baseUrl: "/api/v1/",
  prepareHeaders: (headers, { getState }) => {
    const { token, tenantId } = (getState() as RootState).auth;
    if (token)    headers.set("Authorization", `Bearer ${token}`);
    if (tenantId) headers.set("X-Tenant-Id", tenantId);
    return headers;
  },
});

export const baseQuery: BaseQueryFn<string | FetchArgs, unknown, FetchBaseQueryError> =
  async (args, api, extraOptions) => {
    const result = await rawBaseQuery(args, api, extraOptions);

    if (result.error?.status === 401) {
      api.dispatch(setSessionExpired());
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
      // Plain 403 (wrong role / wrong tenant) — no global side effect;
      // individual pages handle it in their isError branch.
    }

    return result;
  };
