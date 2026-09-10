import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  BoothRentScheduleResponse, CreateBoothRentScheduleRequest, UpdateBoothRentScheduleRequest,
  BoothRentChargeResponse, MarkBoothRentChargeSettledRequest,
} from "./boothRent.types";

export const boothRentApi = createApi({
  reducerPath: "boothRentApi",
  baseQuery,
  tagTypes: ["BoothRentSchedule", "BoothRentCharge"],
  endpoints: (builder) => ({
    getBoothRentSchedules: builder.query<BoothRentScheduleResponse[], { artistId?: string } | void>({
      query: (args) => ({ url: "booth-rent/schedules", params: args?.artistId ? { artistId: args.artistId } : undefined }),
      providesTags: ["BoothRentSchedule"],
    }),
    createBoothRentSchedule: builder.mutation<BoothRentScheduleResponse, CreateBoothRentScheduleRequest>({
      query: (body) => ({ url: "booth-rent/schedules", method: "POST", body }),
      invalidatesTags: ["BoothRentSchedule"],
    }),
    updateBoothRentSchedule: builder.mutation<BoothRentScheduleResponse, { id: string; body: UpdateBoothRentScheduleRequest }>({
      query: ({ id, body }) => ({ url: `booth-rent/schedules/${id}`, method: "PUT", body }),
      invalidatesTags: ["BoothRentSchedule"],
    }),
    getBoothRentCharges: builder.query<BoothRentChargeResponse[], { artistId?: string } | void>({
      query: (args) => ({ url: "booth-rent/charges", params: args?.artistId ? { artistId: args.artistId } : undefined }),
      providesTags: ["BoothRentCharge"],
    }),
    markBoothRentChargeSettled: builder.mutation<BoothRentChargeResponse, { id: string; body: MarkBoothRentChargeSettledRequest }>({
      query: ({ id, body }) => ({ url: `booth-rent/charges/${id}/settle`, method: "POST", body }),
      invalidatesTags: ["BoothRentCharge"],
    }),
  }),
});

export const {
  useGetBoothRentSchedulesQuery,
  useCreateBoothRentScheduleMutation,
  useUpdateBoothRentScheduleMutation,
  useGetBoothRentChargesQuery,
  useMarkBoothRentChargeSettledMutation,
} = boothRentApi;
