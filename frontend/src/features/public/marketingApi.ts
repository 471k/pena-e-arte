import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";

export const marketingApi = createApi({
  reducerPath: "marketingApi",
  baseQuery,
  endpoints: (builder) => ({
    // POST under the hood, modeled as a query so UnsubscribePage can fetch on mount via
    // the auto-fetching query hook instead of a useEffect-triggered mutation call.
    withdrawMarketingOptIn: builder.query<null, string>({
      query: (token) => ({ url: `marketing/unsubscribe?token=${encodeURIComponent(token)}`, method: "POST" }),
    }),
  }),
});

export const { useWithdrawMarketingOptInQuery } = marketingApi;
