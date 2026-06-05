import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type {
  DesignResponse,
  GetDesignsParams,
  CreateDesignRequest,
  DesignRevisionResponse,
  UploadRevisionRequest,
  ReviewRevisionRequest,
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
  }),
});

export const {
  useGetDesignsQuery,
  useCreateDesignMutation,
  useUploadRevisionMutation,
  useGetRevisionsQuery,
  useReviewRevisionMutation,
} = designsApi;
