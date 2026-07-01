import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { RootState } from "@/app/store";
import type { PortfolioImageResponse } from "./publicApi";

const savedBaseQuery = fetchBaseQuery({
  baseUrl: "/api/v1/",
  prepareHeaders: (headers, { getState }) => {
    const token = (getState() as RootState).auth?.token;
    if (token) headers.set("Authorization", `Bearer ${token}`);
    return headers;
  },
});

export const savedImagesApi = createApi({
  reducerPath: "savedImagesApi",
  baseQuery: savedBaseQuery,
  tagTypes: ["SavedImage"],
  endpoints: (builder) => ({
    getSavedImageIds: builder.query<string[], void>({
      query: () => "saved-images/ids",
      providesTags: ["SavedImage"],
    }),
    getSavedImages: builder.query<PortfolioImageResponse[], number>({
      query: (page = 1) => `saved-images?page=${page}`,
      providesTags: ["SavedImage"],
    }),
    saveImage: builder.mutation<void, string>({
      query: (imageId) => ({ url: `saved-images/${imageId}`, method: "POST" }),
      invalidatesTags: ["SavedImage"],
    }),
    unsaveImage: builder.mutation<void, string>({
      query: (imageId) => ({ url: `saved-images/${imageId}`, method: "DELETE" }),
      invalidatesTags: ["SavedImage"],
    }),
  }),
});

export const {
  useGetSavedImageIdsQuery,
  useGetSavedImagesQuery,
  useSaveImageMutation,
  useUnsaveImageMutation,
} = savedImagesApi;
