import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  DesignResponse,
  GetDesignsParams,
  CreateDesignRequest,
  DesignRevisionResponse,
  UploadRevisionRequest,
  ReviewRevisionRequest,
  DesignShareTokenResponse,
  MarkDesignAsCatalogItemRequest,
} from "./design.types";
import type { AppointmentResponse, CreateAppointmentRequest } from "@/features/appointments/appointment.types";

export const designsApi = createApi({
  reducerPath: "designsApi",
  baseQuery,
  tagTypes: ["Design"],
  endpoints: (builder) => ({
    getDesigns: builder.query<DesignResponse[], GetDesignsParams>({
      query: ({ clientId, artistId } = {}) => ({
        url:    "designs",
        params: {
          ...(clientId ? { clientId } : {}),
          ...(artistId ? { artistId } : {}),
        },
      }),
      providesTags: ["Design"],
    }),
    getDesign: builder.query<DesignResponse, string>({
      query: (id) => `designs/${id}`,
      providesTags: (_result, _error, id) => [{ type: "Design", id }],
    }),
    createDesign: builder.mutation<DesignResponse, CreateDesignRequest>({
      query: (body) => ({ url: "designs", method: "POST", body }),
      invalidatesTags: ["Design"],
    }),
    uploadRevision: builder.mutation<DesignRevisionResponse, UploadRevisionRequest>({
      query: ({ designId, fileUrl, notes }) => ({
        url:    `designs/${designId}/revisions`,
        method: "POST",
        body:   { fileUrl, notes },
      }),
      invalidatesTags: ["Design"],
    }),
    getRevisions: builder.query<DesignRevisionResponse[], string>({
      query: (designId) => `designs/${designId}/revisions`,
      providesTags: (_result, _error, designId) => [{ type: "Design", id: designId }],
    }),
    reviewRevision: builder.mutation<DesignRevisionResponse, ReviewRevisionRequest>({
      query: ({ revisionId, approved, notes }) => ({
        url:    `designs/revisions/${revisionId}/review`,
        method: "POST",
        body:   { approved, notes },
      }),
      invalidatesTags: ["Design"],
    }),
    // 2026-11-06: the client asks for an earlier version back (recorded as changes requested on the latest).
    requestVersionAgain: builder.mutation<DesignRevisionResponse, { revisionId: string; notes: string | null }>({
      query: ({ revisionId, notes }) => ({
        url:    `designs/revisions/${revisionId}/request-again`,
        method: "POST",
        body:   { notes },
      }),
      invalidatesTags: ["Design"],
    }),
    // The artist brings an earlier version back as the newest version.
    restoreRevision: builder.mutation<DesignRevisionResponse, { designId: string; revisionId: string }>({
      query: ({ designId, revisionId }) => ({
        url:    `designs/${designId}/revisions/${revisionId}/restore`,
        method: "POST",
      }),
      invalidatesTags: ["Design"],
    }),
    deleteRevision: builder.mutation<void, { designId: string; revisionId: string }>({
      query: ({ designId, revisionId }) => ({
        url:    `designs/${designId}/revisions/${revisionId}`,
        method: "DELETE",
      }),
      invalidatesTags: ["Design"],
    }),
    createShareToken: builder.mutation<DesignShareTokenResponse, string>({
      query: (revisionId) => ({
        url:    `designs/revisions/${revisionId}/share-token`,
        method: "POST",
      }),
    }),
    revokeShareToken: builder.mutation<void, string>({
      query: (tokenId) => ({
        url:    `designs/share-tokens/${tokenId}`,
        method: "DELETE",
      }),
    }),
    markDesignAsCatalogItem: builder.mutation<DesignResponse, { id: string; body: MarkDesignAsCatalogItemRequest }>({
      query: ({ id, body }) => ({ url: `designs/${id}/catalog`, method: "POST", body }),
      invalidatesTags: (_result, _error, { id }) => [{ type: "Design", id }, "Design"],
    }),
    requestCatalogDesign: builder.mutation<AppointmentResponse, { catalogDesignId: string; booking: CreateAppointmentRequest }>({
      query: ({ catalogDesignId, booking }) => ({
        url: `designs/catalog/${catalogDesignId}/request`, method: "POST", body: booking,
      }),
    }),
  }),
});

export const {
  useRequestVersionAgainMutation,
  useRestoreRevisionMutation,
  useGetDesignsQuery,
  useGetDesignQuery,
  useCreateDesignMutation,
  useUploadRevisionMutation,
  useGetRevisionsQuery,
  useReviewRevisionMutation,
  useDeleteRevisionMutation,
  useCreateShareTokenMutation,
  useRevokeShareTokenMutation,
  useMarkDesignAsCatalogItemMutation,
  useRequestCatalogDesignMutation,
} = designsApi;
