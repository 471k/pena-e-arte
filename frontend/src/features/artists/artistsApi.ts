import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";

export interface ArtistResponse {
  id:              string;
  studioId:        string;
  firstName:       string;
  lastName:        string;
  email:           string;
  specializations: string | null;
  hourlyRate:      number | null;
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
}

export const artistsApi = createApi({
  reducerPath: "artistsApi",
  baseQuery,
  tagTypes: ["Artist"],
  endpoints: (builder) => ({
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
    deleteArtist: builder.mutation<void, string>({
      query: (id) => ({ url: `artists/${id}`, method: "DELETE" }),
      invalidatesTags: ["Artist"],
    }),
  }),
});

export const {
  useCreateArtistMutation,
  useGetArtistsQuery,
  useGetArtistByIdQuery,
  useUpdateArtistMutation,
  useDeleteArtistMutation,
} = artistsApi;
