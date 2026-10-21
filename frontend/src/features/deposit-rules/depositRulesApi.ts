import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  DepositRuleDefaultsResponse,
  DepositRuleResponse,
  CreateDepositRuleRequest,
  UpdateDepositRuleRequest,
} from "./depositRule.types";

export const depositRulesApi = createApi({
  reducerPath: "depositRulesApi",
  baseQuery,
  tagTypes: ["DepositRule"],
  endpoints: (builder) => ({
    getDepositRules: builder.query<DepositRuleResponse[], void>({
      query: () => "deposit-rules",
      providesTags: ["DepositRule"],
    }),
    // Platform constants: they only change with a deploy, so keep them for the whole session.
    getDepositRuleDefaults: builder.query<DepositRuleDefaultsResponse, void>({
      query: () => "deposit-rules/defaults",
      keepUnusedDataFor: 24 * 60 * 60,
    }),
    getDepositRuleById: builder.query<DepositRuleResponse, string>({
      query: (id) => `deposit-rules/${id}`,
      providesTags: (_result, _error, id) => [{ type: "DepositRule", id }],
    }),
    createDepositRule: builder.mutation<DepositRuleResponse, CreateDepositRuleRequest>({
      query: (body) => ({ url: "deposit-rules", method: "POST", body }),
      invalidatesTags: ["DepositRule"],
    }),
    updateDepositRule: builder.mutation<DepositRuleResponse, { id: string; body: UpdateDepositRuleRequest }>({
      query: ({ id, body }) => ({ url: `deposit-rules/${id}`, method: "PUT", body }),
      invalidatesTags: (_result, _error, { id }) => [{ type: "DepositRule", id }, "DepositRule"],
    }),
    deleteDepositRule: builder.mutation<void, string>({
      query: (id) => ({ url: `deposit-rules/${id}`, method: "DELETE" }),
      invalidatesTags: ["DepositRule"],
    }),
  }),
});

export const {
  useGetDepositRulesQuery,
  useGetDepositRuleDefaultsQuery,
  useGetDepositRuleByIdQuery,
  useCreateDepositRuleMutation,
  useUpdateDepositRuleMutation,
  useDeleteDepositRuleMutation,
} = depositRulesApi;
