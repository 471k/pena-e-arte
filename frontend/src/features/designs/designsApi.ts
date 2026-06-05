import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type { DesignResponse, GetDesignsParams, CreateDesignRequest } from "./design.types";

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
  }),
});

export const { useGetDesignsQuery, useCreateDesignMutation } = designsApi;
