import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";

interface OnboardingTourStatusResponse {
  hasCompletedTour: boolean;
}

export const onboardingApi = createApi({
  reducerPath: "onboardingApi",
  baseQuery,
  tagTypes: ["OnboardingTourStatus"],
  endpoints: (builder) => ({
    getOnboardingTourStatus: builder.query<OnboardingTourStatusResponse, { role: string }>({
      query: ({ role }) => `onboarding/tour-status?role=${role}`,
      providesTags: (_result, _err, { role }) => [{ type: "OnboardingTourStatus", id: role }],
    }),
    markOnboardingTourComplete: builder.mutation<void, { role: string }>({
      query: (body) => ({ url: "onboarding/tour-complete", method: "POST", body }),
      invalidatesTags: (_result, _err, { role }) => [{ type: "OnboardingTourStatus", id: role }],
    }),
  }),
});

export const {
  useGetOnboardingTourStatusQuery,
  useMarkOnboardingTourCompleteMutation,
} = onboardingApi;
