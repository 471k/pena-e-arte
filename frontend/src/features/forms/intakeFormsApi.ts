import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  IntakeFormResponse,
  SubmitIntakeFormRequest,
  GetIntakeFormsParams,
  IntakeFormTemplateResponse,
  UpsertIntakeFormTemplateRequest,
} from "./form.types";

export const intakeFormsApi = createApi({
  reducerPath: "intakeFormsApi",
  baseQuery,
  tagTypes: ["IntakeForm", "IntakeFormTemplate"],
  endpoints: (builder) => ({
    getIntakeForms: builder.query<IntakeFormResponse[], GetIntakeFormsParams>({
      query: ({ clientId, appointmentId } = {}) => ({
        url:    "intake-forms",
        params: {
          ...(clientId      ? { clientId }      : {}),
          ...(appointmentId ? { appointmentId } : {}),
        },
      }),
      providesTags: ["IntakeForm"],
    }),
    getIntakeFormById: builder.query<IntakeFormResponse, string>({
      query: (id) => `intake-forms/${id}`,
      providesTags: (_result, _error, id) => [{ type: "IntakeForm", id }],
    }),
    submitIntakeForm: builder.mutation<IntakeFormResponse, SubmitIntakeFormRequest>({
      query: (body) => ({ url: "intake-forms", method: "POST", body }),
      invalidatesTags: ["IntakeForm"],
    }),
    getActiveIntakeFormTemplate: builder.query<IntakeFormTemplateResponse | null, void>({
      query: () => "intake-forms/active-template",
      providesTags: ["IntakeFormTemplate"],
    }),
    getMyIntakeFormTemplate: builder.query<IntakeFormTemplateResponse | null, void>({
      query: () => "intake-forms/template/mine",
      providesTags: ["IntakeFormTemplate"],
    }),
    upsertIntakeFormTemplate: builder.mutation<IntakeFormTemplateResponse, UpsertIntakeFormTemplateRequest>({
      query: (body) => ({ url: "intake-forms/template", method: "PUT", body }),
      invalidatesTags: ["IntakeFormTemplate"],
    }),
  }),
});

export const {
  useGetIntakeFormsQuery,
  useGetIntakeFormByIdQuery,
  useSubmitIntakeFormMutation,
  useGetActiveIntakeFormTemplateQuery,
  useGetMyIntakeFormTemplateQuery,
  useUpsertIntakeFormTemplateMutation,
} = intakeFormsApi;
