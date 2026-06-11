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
  userId:    string | null;
}

export interface ClientProfileResponse {
  id:               string;
  clientId:         string;
  studioId:         string;
  dateOfBirth:      string | null;
  medicalNotes:     string | null;
  allergies:        string | null;
  bodyMapLocations: string[];
  updatedAt:        string;
}

export interface CreateClientRequest {
  firstName: string;
  lastName:  string;
  email:     string;
  phone:     string | null;
}

export interface UpsertClientProfileRequest {
  dateOfBirth:  string | null;
  medicalNotes: string | null;
  allergies:    string | null;
}

export interface TattooRecordResponse {
  id:            string;
  clientId:      string;
  artistId:      string;
  appointmentId: string | null;
  description:   string;
  bodyLocation:  string;
  photoUrls:     string[];
  completedAt:   string;
  createdAt:     string;
}

export interface AddTattooRecordRequest {
  artistId:      string;
  appointmentId: string | null;
  description:   string;
  bodyLocation:  string;
  photoUrls:     string[];
  completedAt:   string;
}

export interface UpdateTattooRecordRequest {
  description:  string;
  bodyLocation: string;
  photoUrls:    string[];
  completedAt:  string;
}

export interface PortableTattooRecord {
  bodyLocation:    string;
  photoUrls:       string[];
  description:     string;
  completedAt:     string;
  artistFirstName: string;
}

export interface PortableClientProfile {
  displayName:      string;
  bodyMapLocations: string[];
  tattooHistory:    PortableTattooRecord[];
}

export const clientsApi = createApi({
  reducerPath: "clientsApi",
  baseQuery,
  tagTypes: ["Client", "ClientProfile", "TattooRecord", "PortableProfile"],
  endpoints: (builder) => ({
    getClients: builder.query<ClientResponse[], string | undefined>({
      query: (search) => ({
        url: "clients",
        params: search ? { search } : undefined,
      }),
      providesTags: ["Client"],
    }),
    getClientById: builder.query<ClientResponse, string>({
      query: (id) => `clients/${id}`,
      providesTags: (_result, _error, id) => [{ type: "Client", id }],
    }),
    createClient: builder.mutation<ClientResponse, CreateClientRequest>({
      query: (body) => ({ url: "clients", method: "POST", body }),
      invalidatesTags: ["Client"],
    }),
    getClientProfile: builder.query<ClientProfileResponse, string>({
      query: (clientId) => `clients/${clientId}/profile`,
      providesTags: (_result, _error, clientId) => [{ type: "ClientProfile", id: clientId }],
    }),
    upsertClientProfile: builder.mutation<
      ClientProfileResponse,
      { clientId: string; body: UpsertClientProfileRequest }
    >({
      query: ({ clientId, body }) => ({
        url: `clients/${clientId}/profile`,
        method: "PUT",
        body,
      }),
      invalidatesTags: (_result, _error, { clientId }) => [
        { type: "ClientProfile", id: clientId },
      ],
    }),
    getTattooRecords: builder.query<TattooRecordResponse[], string>({
      query: (clientId) => `clients/${clientId}/tattoos`,
      providesTags: (_result, _error, clientId) => [{ type: "TattooRecord", id: clientId }],
    }),
    getTattooRecord: builder.query<
      TattooRecordResponse,
      { clientId: string; tattooId: string }
    >({
      query: ({ clientId, tattooId }) => `clients/${clientId}/tattoos/${tattooId}`,
      providesTags: (_result, _error, { tattooId }) => [{ type: "TattooRecord", id: tattooId }],
    }),
    addTattooRecord: builder.mutation<
      TattooRecordResponse,
      { clientId: string; body: AddTattooRecordRequest }
    >({
      query: ({ clientId, body }) => ({
        url: `clients/${clientId}/tattoos`,
        method: "POST",
        body,
      }),
      invalidatesTags: (_result, _error, { clientId }) => [{ type: "TattooRecord", id: clientId }],
    }),
    updateTattooRecord: builder.mutation<
      TattooRecordResponse,
      { clientId: string; tattooId: string; body: UpdateTattooRecordRequest }
    >({
      query: ({ clientId, tattooId, body }) => ({
        url: `clients/${clientId}/tattoos/${tattooId}`,
        method: "PATCH",
        body,
      }),
      invalidatesTags: (_result, _error, { clientId, tattooId }) => [
        { type: "TattooRecord", id: tattooId },
        { type: "TattooRecord", id: clientId },
      ],
    }),
    deleteTattooRecord: builder.mutation<
      void,
      { clientId: string; tattooId: string }
    >({
      query: ({ clientId, tattooId }) => ({
        url: `clients/${clientId}/tattoos/${tattooId}`,
        method: "DELETE",
      }),
      invalidatesTags: (_result, _error, { clientId }) => [{ type: "TattooRecord", id: clientId }],
    }),
    updateBodyMap: builder.mutation<
      ClientProfileResponse,
      { clientId: string; locations: string[] }
    >({
      query: ({ clientId, locations }) => ({
        url: `clients/${clientId}/profile/body-map`,
        method: "PATCH",
        body: { locations },
      }),
      invalidatesTags: (_result, _error, { clientId }) => [
        { type: "ClientProfile", id: clientId },
      ],
    }),
    updatePortableProfileOptIn: builder.mutation<void, boolean>({
      query: (optIn) => ({
        url: "clients/me/portable-profile",
        method: "PATCH",
        body: { optIn },
      }),
      invalidatesTags: ["PortableProfile"],
    }),
    getPortableProfile: builder.query<PortableClientProfile | null, string>({
      query: (userId) => `clients/${userId}/portable-profile`,
      providesTags: (_result, _error, userId) => [{ type: "PortableProfile", id: userId }],
    }),
  }),
});

export const {
  useGetClientsQuery,
  useGetClientByIdQuery,
  useCreateClientMutation,
  useGetClientProfileQuery,
  useUpsertClientProfileMutation,
  useUpdateBodyMapMutation,
  useGetTattooRecordsQuery,
  useGetTattooRecordQuery,
  useAddTattooRecordMutation,
  useUpdateTattooRecordMutation,
  useDeleteTattooRecordMutation,
  useUpdatePortableProfileOptInMutation,
  useGetPortableProfileQuery,
} = clientsApi;
