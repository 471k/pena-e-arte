import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  ConsentFormResponse,
  ConsentFormDetailResponse,
  ConsentTemplateResponse,
  SignConsentFormRequest,
  GetConsentFormsParams,
} from "./form.types";

export const consentFormsApi = createApi({
  reducerPath: "consentFormsApi",
  baseQuery,
  tagTypes: ["ConsentForm"],
  endpoints: (builder) => ({
    getConsentForms: builder.query<ConsentFormResponse[], GetConsentFormsParams>({
      query: ({ clientId, appointmentId } = {}) => ({
        url:    "consent-forms",
        params: {
          ...(clientId      ? { clientId }      : {}),
          ...(appointmentId ? { appointmentId } : {}),
        },
      }),
      providesTags: ["ConsentForm"],
    }),
    getConsentFormById: builder.query<ConsentFormDetailResponse, string>({
      query: (id) => `consent-forms/${id}`,
      providesTags: (_result, _error, id) => [{ type: "ConsentForm", id }],
    }),
    getActiveConsentTemplate: builder.query<ConsentTemplateResponse, { kind: string }>({
      query: ({ kind }) => ({ url: "consent-forms/active-template", params: { kind } }),
    }),
    signConsentForm: builder.mutation<ConsentFormResponse, SignConsentFormRequest>({
      query: (body) => ({ url: "consent-forms", method: "POST", body }),
      invalidatesTags: ["ConsentForm"],
    }),
  }),
});

export const {
  useGetConsentFormsQuery,
  useGetConsentFormByIdQuery,
  useGetActiveConsentTemplateQuery,
  useSignConsentFormMutation,
} = consentFormsApi;
