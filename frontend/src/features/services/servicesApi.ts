import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  ServiceResponse,
  CreateServiceRequest,
  UpdateServiceRequest,
} from "./service.types";

export const servicesApi = createApi({
  reducerPath: "servicesApi",
  baseQuery,
  tagTypes: ["Service"],
  endpoints: (builder) => ({
    getServices: builder.query<ServiceResponse[], void>({
      query: () => "services",
      providesTags: ["Service"],
    }),
    getServiceById: builder.query<ServiceResponse, string>({
      query: (id) => `services/${id}`,
      providesTags: (_result, _error, id) => [{ type: "Service", id }],
    }),
    createService: builder.mutation<ServiceResponse, CreateServiceRequest>({
      query: (body) => ({ url: "services", method: "POST", body }),
      invalidatesTags: ["Service"],
    }),
    updateService: builder.mutation<ServiceResponse, { id: string; body: UpdateServiceRequest }>({
      query: ({ id, body }) => ({ url: `services/${id}`, method: "PUT", body }),
      invalidatesTags: (_result, _error, { id }) => [{ type: "Service", id }, "Service"],
    }),
    deleteService: builder.mutation<void, string>({
      query: (id) => ({ url: `services/${id}`, method: "DELETE" }),
      invalidatesTags: ["Service"],
    }),
  }),
});

export const {
  useGetServicesQuery,
  useGetServiceByIdQuery,
  useCreateServiceMutation,
  useUpdateServiceMutation,
  useDeleteServiceMutation,
} = servicesApi;
