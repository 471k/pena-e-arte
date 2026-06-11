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
} from "./design.types";

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
  }),
});

export const {
  useGetDesignsQuery,
  useCreateDesignMutation,
  useUploadRevisionMutation,
  useGetRevisionsQuery,
  useReviewRevisionMutation,
  useDeleteRevisionMutation,
  useCreateShareTokenMutation,
  useRevokeShareTokenMutation,
} = designsApi;
