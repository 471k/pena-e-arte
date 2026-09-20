import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import { consentFormsApi } from "@/features/forms/consentFormsApi";

export interface ClientResponse {
  id:                 string;
  studioId:           string;
  firstName:          string;
  lastName:           string;
  email:              string;
  phone:              string | null;
  createdAt:          string;
  userId:             string | null;
  artistId:           string | null;
  artistName:         string | null;
  erasureRequestedAt: string | null;
  archivedAt:         string | null;
}

export interface UpdateMyClientRequest {
  firstName: string;
  lastName:  string;
  /** E.164, or null to remove the number. Email is intentionally absent — it changes only via the change-email flow. */
  phone:     string | null;
}

export interface GetClientsParams {
  search?:          string;
  includeArchived?: boolean;
}

export interface ClientDataExportProfile {
  firstName:    string;
  lastName:     string;
  email:        string;
  phone:        string | null;
  createdAt:    string;
  dateOfBirth:  string | null;
  allergies:    string | null;
  medicalNotes: string | null;
}

export interface ClientDataExportAppointment {
  id:             string;
  date:           string;
  endDate:        string;
  status:         string;
  paymentAmount:  number | null;
  paymentStatus:  string | null;
}

export interface ClientDataExportConsentForm {
  id:                string;
  appointmentId:     string;
  signedAt:          string | null;
  signedDocumentUrl: string | null;
}

export interface ClientDataExportTattooRecord {
  id:           string;
  description:  string;
  bodyLocation: string;
  completedAt:  string;
  photoUrls:    string[];
}

export interface ClientDataExportStudioSection {
  studioId:      string;
  studioName:    string;
  profile:       ClientDataExportProfile;
  appointments:  ClientDataExportAppointment[];
  consentForms:  ClientDataExportConsentForm[];
  tattooRecords: ClientDataExportTattooRecord[];
}

export interface ClientDataExportResponse {
  studios: ClientDataExportStudioSection[];
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
  allowCrossTenantRead: boolean;
}

export interface CreateClientRequest {
  firstName: string;
  lastName:  string;
  email:     string;
  phone:     string | null;
  artistId:  string;
}

export interface UpdateClientArtistRequest {
  artistId: string | null;
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
    getMyClient: builder.query<ClientResponse, void>({
      query: () => "clients/me",
      providesTags: [{ type: "Client", id: "me" }],
    }),
    getMyClientProfile: builder.query<ClientProfileResponse, void>({
      query: () => "clients/me/profile",
      providesTags: [{ type: "ClientProfile", id: "me" }],
    }),
    getMyTattooRecords: builder.query<TattooRecordResponse[], void>({
      query: () => "clients/me/tattoos",
      providesTags: [{ type: "TattooRecord", id: "me" }],
    }),
    getClients: builder.query<ClientResponse[], GetClientsParams | string | undefined>({
      query: (params) => {
        const { search, includeArchived } =
          typeof params === "string" ? { search: params, includeArchived: undefined } : (params ?? {});
        return {
          url: "clients",
          params: {
            ...(search ? { search } : {}),
            ...(includeArchived ? { includeArchived: true } : {}),
          },
        };
      },
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
    updateClientArtist: builder.mutation<
      ClientResponse,
      { clientId: string; body: UpdateClientArtistRequest }
    >({
      query: ({ clientId, body }) => ({
        url: `clients/${clientId}/artist`,
        method: "PATCH",
        body,
      }),
      invalidatesTags: (_result, _error, { clientId }) => [
        { type: "Client", id: clientId },
        "Client",
      ],
    }),
    getClientProfile: builder.query<ClientProfileResponse | null, string>({
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
    updateMyClient: builder.mutation<ClientResponse, UpdateMyClientRequest>({
      query: (body) => ({
        url: "clients/me",
        method: "PATCH",
        body,
      }),
      invalidatesTags: [{ type: "Client", id: "me" }],
    }),
    updateMyBodyMap: builder.mutation<ClientProfileResponse, string[]>({
      query: (locations) => ({
        url: "clients/me/profile/body-map",
        method: "PATCH",
        body: { locations },
      }),
      invalidatesTags: [{ type: "ClientProfile", id: "me" }],
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
    // Client self-service "delete my account" (GDPR Art. 17). No id in the URL — the backend
    // resolves the caller's own client from the JWT, so only the caller's data can be erased.
    requestMyDataErasure: builder.mutation<void, void>({
      query: () => ({ url: "clients/me/erase-data", method: "POST" }),
    }),
    // Owner/support-initiated erasure of a specific client (GDPR Art. 17).
    requestDataErasure: builder.mutation<void, string>({
      query: (clientId) => ({ url: `clients/${clientId}/erase-data`, method: "POST" }),
      invalidatesTags: (_result, _error, clientId) => [
        { type: "Client", id: clientId },
        "Client",
        { type: "ClientProfile", id: clientId },
      ],
      // Erasure also soft-deletes the client's consent forms, but ConsentForm lives in a
      // separate RTK Query slice — invalidatesTags here can't reach it.
      async onQueryStarted(_clientId, { dispatch, queryFulfilled }) {
        try {
          await queryFulfilled;
          dispatch(consentFormsApi.util.invalidateTags(["ConsentForm"]));
        } catch {
          // Mutation failed — nothing to invalidate.
        }
      },
    }),
    // Non-destructive "remove from list" — reversible, keeps all related data intact.
    archiveClient: builder.mutation<void, string>({
      query: (clientId) => ({ url: `clients/${clientId}/archive`, method: "POST" }),
      invalidatesTags: (_result, _error, clientId) => [{ type: "Client", id: clientId }, "Client"],
    }),
    restoreClient: builder.mutation<void, string>({
      query: (clientId) => ({ url: `clients/${clientId}/restore`, method: "POST" }),
      invalidatesTags: (_result, _error, clientId) => [{ type: "Client", id: clientId }, "Client"],
    }),
    // Client self-service "export my data" — fans out across every studio the caller belongs to.
    exportMyData: builder.query<ClientDataExportResponse, void>({
      query: () => "clients/me/export",
    }),
    // Owner-facing cancel of a pending erasure request during the retention grace window.
    cancelDataErasure: builder.mutation<void, string>({
      query: (clientId) => ({ url: `clients/${clientId}/cancel-erasure`, method: "POST" }),
      invalidatesTags: (_result, _error, clientId) => [
        { type: "Client", id: clientId },
        "Client",
      ],
    }),
  }),
});

export const {
  useGetMyClientQuery,
  useGetMyClientProfileQuery,
  useGetMyTattooRecordsQuery,
  useGetClientsQuery,
  useGetClientByIdQuery,
  useCreateClientMutation,
  useUpdateClientArtistMutation,
  useGetClientProfileQuery,
  useUpsertClientProfileMutation,
  useUpdateBodyMapMutation,
  useUpdateMyClientMutation,
  useUpdateMyBodyMapMutation,
  useGetTattooRecordsQuery,
  useGetTattooRecordQuery,
  useAddTattooRecordMutation,
  useUpdateTattooRecordMutation,
  useDeleteTattooRecordMutation,
  useUpdatePortableProfileOptInMutation,
  useGetPortableProfileQuery,
  useRequestMyDataErasureMutation,
  useRequestDataErasureMutation,
  useArchiveClientMutation,
  useRestoreClientMutation,
  useExportMyDataQuery,
  useLazyExportMyDataQuery,
  useCancelDataErasureMutation,
} = clientsApi;
