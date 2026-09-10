import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type { CampaignResponse, CreateCampaignRequest } from "./campaigns.types";

export const campaignsApi = createApi({
  reducerPath: "campaignsApi",
  baseQuery,
  tagTypes: ["Campaign"],
  endpoints: (builder) => ({
    getCampaigns: builder.query<CampaignResponse[], void>({
      query: () => "campaigns",
      providesTags: ["Campaign"],
    }),
    createCampaign: builder.mutation<CampaignResponse, CreateCampaignRequest>({
      query: (body) => ({ url: "campaigns", method: "POST", body }),
      invalidatesTags: ["Campaign"],
    }),
    sendCampaign: builder.mutation<CampaignResponse, string>({
      query: (id) => ({ url: `campaigns/${id}/send`, method: "POST" }),
      invalidatesTags: ["Campaign"],
    }),
  }),
});

export const {
  useGetCampaignsQuery,
  useCreateCampaignMutation,
  useSendCampaignMutation,
} = campaignsApi;
