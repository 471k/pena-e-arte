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
  endpoints: (builder) => ({
    getArtists: builder.query<ArtistResponse[], void>({
      query: () => "artists",
    }),
  }),
});

export const { useGetArtistsQuery } = artistsApi;
