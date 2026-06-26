import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { RootState } from "@/app/store";

export interface PublicArtistSummary {
  artistId:        string;
  name:            string;
  slug:            string;
  bio:             string | null;
  profileImageUrl: string | null;
  specializations: string | null;
  averageRating:   number | null;
  reviewCount:     number;
}

export interface PublicStudioResponse {
  studioId:        string;
  name:            string;
  slug:            string;
  city:            string;
  description:     string | null;
  coverImageUrl:   string | null;
  phoneNumber:     string | null;
  instagramHandle: string | null;
  averageRating:   number | null;
  reviewCount:     number;
  galleryImages:   string[];
  artists:         PublicArtistSummary[];
  showBookingCta:  boolean;
}

export interface PublicArtistResponse {
  artistId:        string;
  name:            string;
  slug:            string;
  bio:             string | null;
  profileImageUrl: string | null;
  portfolioImages: string[];
  specializations: string | null;
  hourlyRate:      number | null;
  averageRating:   number | null;
  reviewCount:     number;
  studioName:      string;
  studioSlug:      string;
  showBookingCta:  boolean;
  isOwnProfile:    boolean;
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

export interface PortfolioImageResponse {
  imageUrl:      string;
  artistName:    string;
  artistSlug:    string;
  studioName:    string;
  studioSlug:    string;
  averageRating: number | null;
  reviewCount:   number;
  distanceKm:    number | null;
  viewCount:     number;
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

// Attaches the JWT when available so authenticated endpoints (e.g. POST reviews)
// work without triggering the global session-expired redirect for anonymous calls.
const publicBaseQuery = fetchBaseQuery({
  baseUrl: "/api/v1/public/",
  prepareHeaders: (headers, { getState }) => {
    const token = (getState() as RootState).auth.token;
    if (token) headers.set("Authorization", `Bearer ${token}`);
    return headers;
  },
});

export const publicApi = createApi({
  reducerPath: "publicApi",
  baseQuery: publicBaseQuery,
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
    getPortfolioFeed: builder.query<
      PortfolioImageResponse[],
      { lat?: number; lng?: number; radiusKm?: number; page: number }
    >({
      query: ({ lat, lng, radiusKm = 50, page }) => {
        const params = new URLSearchParams({ radiusKm: String(radiusKm), page: String(page) });
        if (lat != null) params.set("lat", String(lat));
        if (lng != null) params.set("lng", String(lng));
        return `portfolio/feed?${params.toString()}`;
      },
      // Location/radius changes start a fresh cache entry; page changes append.
      serializeQueryArgs: ({ queryArgs: { lat, lng, radiusKm } }) =>
        `portfolio-feed:${lat ?? ""}:${lng ?? ""}:${radiusKm ?? 50}`,
      merge: (currentCache, newItems) => {
        currentCache.push(...newItems);
      },
      forceRefetch: ({ currentArg, previousArg }) =>
        currentArg?.page !== previousArg?.page,
    }),
    recordArtistView: builder.mutation<void, string>({
      query: (slug) => ({ url: `artists/${slug}/view`, method: "POST" }),
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
  useGetPortfolioFeedQuery,
  useRecordArtistViewMutation,
  useGetStudioReviewsQuery,
  useGetArtistReviewsQuery,
  useCreateStudioReviewMutation,
  useCreateArtistReviewMutation,
} = publicApi;
