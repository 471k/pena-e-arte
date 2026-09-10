import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type { WaitlistEntryResponse, JoinWaitlistRequest } from "./waitlist.types";

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
    markWaitlistEntryBooked: builder.mutation<void, string>({
      query: (id) => ({ url: `waitlist/${id}/mark-booked`, method: "POST" }),
      invalidatesTags: ["WaitlistEntry"],
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
  useMarkWaitlistEntryBookedMutation,
  useCancelWaitlistEntryMutation,
} = waitlistApi;
