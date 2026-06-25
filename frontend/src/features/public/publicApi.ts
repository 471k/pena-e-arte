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

export interface NearbyStudioResponse {
  studioId:      string;
  name:          string;
  slug:          string;
  city:          string;
  coverImageUrl: string | null;
  distanceKm:    number;
  artistCount:   number;
  averageRating: number | null;  // null = no reviews
  reviewCount:   number;
}

export interface ReviewResponse {
  id:         string;
  authorName: string;
  rating:     number;
  body:       string;
  createdAt:  string;
}

export interface NearbyStudiosArgs {
  lat:      number;
  lng:      number;
  radiusKm: number;
}

export interface CreateReviewArgs {
  slug:   string;
  rating: number;
  body:   string;
}

export const publicApi = createApi({
  reducerPath: "publicApi",
  baseQuery: fetchBaseQuery({ baseUrl: "/api/v1/public/" }),
  tagTypes: ["PublicStudio", "PublicArtist", "SharedDesign", "NearbyStudios", "StudioReviews", "ArtistReviews"],
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
    getNearbyStudios: builder.query<NearbyStudioResponse[], NearbyStudiosArgs>({
      query: ({ lat, lng, radiusKm }) =>
        `studios/nearby?lat=${lat}&lng=${lng}&radiusKm=${radiusKm}`,
      providesTags: ["NearbyStudios"],
    }),
    getStudioReviews: builder.query<ReviewResponse[], string>({
      query: (slug) => `studios/${slug}/reviews`,
      providesTags: (_result, _err, slug) => [{ type: "StudioReviews", id: slug }],
    }),
    getArtistReviews: builder.query<ReviewResponse[], string>({
      query: (slug) => `artists/${slug}/reviews`,
      providesTags: (_result, _err, slug) => [{ type: "ArtistReviews", id: slug }],
    }),
    createStudioReview: builder.mutation<void, CreateReviewArgs>({
      query: ({ slug, rating, body }) => ({
        url:    `studios/${slug}/reviews`,
        method: "POST",
        body:   { rating, body },
      }),
      invalidatesTags: (_result, _err, { slug }) => [{ type: "StudioReviews", id: slug }],
    }),
    createArtistReview: builder.mutation<void, CreateReviewArgs>({
      query: ({ slug, rating, body }) => ({
        url:    `artists/${slug}/reviews`,
        method: "POST",
        body:   { rating, body },
      }),
      invalidatesTags: (_result, _err, { slug }) => [{ type: "ArtistReviews", id: slug }],
    }),
  }),
});

export const {
  useGetPublicStudioQuery,
  useGetPublicArtistQuery,
  useGetSharedDesignQuery,
  useGetNearbyStudiosQuery,
  useGetStudioReviewsQuery,
  useGetArtistReviewsQuery,
  useCreateStudioReviewMutation,
  useCreateArtistReviewMutation,
} = publicApi;
