import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type { ClientReferralCodeResponse, ClientReferralRewardResponse } from "./clientReferrals.types";

export const clientReferralsApi = createApi({
  reducerPath: "clientReferralsApi",
  baseQuery,
  tagTypes: ["ClientReferralCode", "ClientReferralReward"],
  endpoints: (builder) => ({
    // POST under the hood (idempotent get-or-create), but modeled as a query — not a
    // mutation — so the "Refer a friend" card can fetch on mount via the auto-fetching
    // query hook instead of a useEffect-triggered mutation call.
    getOrCreateMyReferralCode: builder.query<ClientReferralCodeResponse, void>({
      query: () => ({ url: "clients/me/referrals/code", method: "POST" }),
      providesTags: ["ClientReferralCode"],
    }),
    getMyReferralRewards: builder.query<ClientReferralRewardResponse[], void>({
      query: () => "clients/me/referrals/rewards",
      providesTags: ["ClientReferralReward"],
    }),
  }),
});

export const {
  useGetOrCreateMyReferralCodeQuery,
  useGetMyReferralRewardsQuery,
} = clientReferralsApi;
