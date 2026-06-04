import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { RootState } from "@/app/store";

export interface ArtistResponse {
  id:              string;
  studioId:        string;
  firstName:       string;
  lastName:        string;
  email:           string;
  specializations: string | null;
  createdAt:       string;
  updatedAt:       string;
}

export interface UpdateArtistRequest {
  firstName:       string;
  lastName:        string;
  email:           string;
  specializations: string | null;
}

export const artistsApi = createApi({
  reducerPath: "artistsApi",
  baseQuery: fetchBaseQuery({
    baseUrl: "/api/v1/",
    prepareHeaders: (headers, { getState }) => {
      const { token, tenantId } = (getState() as RootState).auth;
      if (token)    headers.set("Authorization", `Bearer ${token}`);
      if (tenantId) headers.set("X-Tenant-Id", tenantId);
      return headers;
    },
  }),
  tagTypes: ["Artist"],
  endpoints: (builder) => ({
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
  useGetArtistsQuery,
  useGetArtistByIdQuery,
  useUpdateArtistMutation,
  useDeleteArtistMutation,
} = artistsApi;
