import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type { WaitlistEntryResponse, JoinWaitlistRequest, WaitlistClaimResponse } from "./waitlist.types";

export const waitlistApi = createApi({
  reducerPath: "waitlistApi",
  baseQuery,
  tagTypes: ["WaitlistEntry"],
  endpoints: (builder) => ({
    joinWaitlist: builder.mutation<WaitlistEntryResponse, JoinWaitlistRequest>({
      query: (body) => ({ url: "waitlist", method: "POST", body }),
      invalidatesTags: ["WaitlistEntry"],
    }),
    getMyWaitlistEntries: builder.query<WaitlistEntryResponse[], void>({
      query: () => "waitlist/mine",
      providesTags: ["WaitlistEntry"],
    }),
    getWaitlist: builder.query<WaitlistEntryResponse[], { artistId?: string } | void>({
      query: (args) => ({ url: "waitlist", params: args?.artistId ? { artistId: args.artistId } : undefined }),
      providesTags: ["WaitlistEntry"],
    }),
    // C09 claim page read. POST so the token travels in the body: it reaches the page in the URL
    // fragment, which no server sees, and should never be put into a URL a server does see.
    getWaitlistClaim: builder.query<WaitlistClaimResponse, string>({
      query: (token) => ({ url: "public/waitlist/claim", method: "POST", body: { token } }),
    }),
    cancelWaitlistEntry: builder.mutation<void, string>({
      query: (id) => ({ url: `waitlist/${id}`, method: "DELETE" }),
      invalidatesTags: ["WaitlistEntry"],
    }),
  }),
});

export const {
  useJoinWaitlistMutation,
  useGetMyWaitlistEntriesQuery,
  useGetWaitlistQuery,
  useGetWaitlistClaimQuery,
  useCancelWaitlistEntryMutation,
} = waitlistApi;
