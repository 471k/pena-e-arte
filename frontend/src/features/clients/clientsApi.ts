import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";

export interface ClientResponse {
  id:        string;
  studioId:  string;
  firstName: string;
  lastName:  string;
  email:     string;
  phone:     string | null;
  createdAt: string;
}

export interface CreateClientRequest {
  firstName: string;
  lastName:  string;
  email:     string;
  phone:     string | null;
}

export const clientsApi = createApi({
  reducerPath: "clientsApi",
  baseQuery,
  tagTypes: ["Client"],
  endpoints: (builder) => ({
    getClients: builder.query<ClientResponse[], string | undefined>({
      query: (search) => ({
        url: "clients",
        params: search ? { search } : undefined,
      }),
      providesTags: ["Client"],
    }),
    createClient: builder.mutation<ClientResponse, CreateClientRequest>({
      query: (body) => ({ url: "clients", method: "POST", body }),
      invalidatesTags: ["Client"],
    }),
  }),
});

export const {
  useGetClientsQuery,
  useCreateClientMutation,
} = clientsApi;
