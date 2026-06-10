import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";

export interface PublicArtistSummary {
  artistId: string;
  name:     string;
  slug:     string;
  bio:      string | null;
}

export interface PublicStudioResponse {
  studioId:      string;
  name:          string;
  slug:          string;
  city:          string;
  description:   string | null;
  coverImageUrl: string | null;
  artists:       PublicArtistSummary[];
  showBookingCta: boolean;
}

export interface PublicArtistResponse {
  artistId:       string;
  name:           string;
  slug:           string;
  bio:            string | null;
  portfolioImages: string[];
  studioName:     string;
  studioSlug:     string;
  showBookingCta: boolean;
}

export interface SharedDesignResponse {
  imageUrl:   string;
  title:      string;
  studioName: string;
  studioSlug: string;
  expiresAt:  string;
}

export const publicApi = createApi({
  reducerPath: "publicApi",
  baseQuery: fetchBaseQuery({ baseUrl: "/api/v1/public/" }),
  tagTypes: ["PublicStudio", "PublicArtist", "SharedDesign"],
  endpoints: (builder) => ({
    getPublicStudio: builder.query<PublicStudioResponse, string>({
      query: (slug) => `studios/${slug}`,
      providesTags: ["PublicStudio"],
    }),
    getPublicArtist: builder.query<PublicArtistResponse, string>({
      query: (slug) => `artists/${slug}`,
      providesTags: ["PublicArtist"],
    }),
    getSharedDesign: builder.query<SharedDesignResponse, string>({
      query: (token) => `designs/share/${token}`,
      providesTags: ["SharedDesign"],
    }),
  }),
});

export const {
  useGetPublicStudioQuery,
  useGetPublicArtistQuery,
  useGetSharedDesignQuery,
} = publicApi;
