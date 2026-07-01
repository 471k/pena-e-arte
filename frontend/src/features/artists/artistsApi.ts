import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";

export interface ArtistResponse {
  id:              string;
  studioId:        string;
  userId:          string | null;
  firstName:       string;
  lastName:        string;
  email:           string;
  specializations: string | null;
  hourlyRate:      number | null;
  isActive:        boolean;
  avatarUrl:       string | null;
  portfolioImages: string[];
  slug:            string | null;
  createdAt:       string;
  updatedAt:       string;
}

export interface CreateArtistRequest {
  firstName:       string;
  lastName:        string;
  email:           string;
  specializations: string | null;
  hourlyRate:      number | null;
}

export interface UpdateArtistRequest {
  firstName:       string;
  lastName:        string;
  email:           string;
  specializations: string | null;
  hourlyRate:      number | null;
  slug?:           string;
}

export const artistsApi = createApi({
  reducerPath: "artistsApi",
  baseQuery,
  tagTypes: ["Artist"],
  endpoints: (builder) => ({
    getMyArtist: builder.query<ArtistResponse, void>({
      query: () => "artists/me",
      providesTags: ["Artist"],
    }),
    createArtist: builder.mutation<ArtistResponse, CreateArtistRequest>({
      query: (body) => ({ url: "artists", method: "POST", body }),
      invalidatesTags: ["Artist"],
    }),
    getArtists: builder.query<ArtistResponse[], string | undefined>({
      query: (search) => ({
        url: "artists",
        params: search ? { search } : undefined,
      }),
      providesTags: ["Artist"],
    }),
    getArtistById: builder.query<ArtistResponse, string>({
      query: (id) => `artists/${id}`,
      providesTags: (_result, _error, id) => [{ type: "Artist", id }],
    }),
    updateArtist: builder.mutation<ArtistResponse, { id: string; body: UpdateArtistRequest }>({
      query: ({ id, body }) => ({ url: `artists/${id}`, method: "PUT", body }),
      invalidatesTags: (_result, _error, { id }) => [{ type: "Artist", id }, "Artist"],
    }),
    updateArtistPortfolio: builder.mutation<ArtistResponse, { id: string; imageUrls: string[] }>({
      query: ({ id, imageUrls }) => ({
        url:    `artists/${id}/portfolio-images`,
        method: "PUT",
        body:   { imageUrls },
      }),
      async onQueryStarted({ id }, { dispatch, queryFulfilled }) {
        try {
          const { data: updated } = await queryFulfilled;
          dispatch(artistsApi.util.updateQueryData("getArtistById", id, () => updated));
          dispatch(artistsApi.util.updateQueryData("getMyArtist", undefined, () => updated));
        } catch {
          // Mutation failed — invalidatesTags below will handle any cleanup.
        }
      },
      invalidatesTags: (_result, _error, { id }) => [{ type: "Artist", id }, "Artist"],
    }),
    deleteArtist: builder.mutation<void, string>({
      query: (id) => ({ url: `artists/${id}`, method: "DELETE" }),
      invalidatesTags: ["Artist"],
    }),
  }),
});

export const {
  useGetMyArtistQuery,
  useCreateArtistMutation,
  useGetArtistsQuery,
  useGetArtistByIdQuery,
  useUpdateArtistMutation,
  useUpdateArtistPortfolioMutation,
  useDeleteArtistMutation,
} = artistsApi;
