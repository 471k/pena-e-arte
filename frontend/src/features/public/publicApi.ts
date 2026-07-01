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

export interface ArtistPortfolioImage {
  imageId:  string;
  imageUrl: string;
}

export interface PublicArtistResponse {
  artistId:        string;
  name:            string;
  slug:            string;
  bio:             string | null;
  profileImageUrl: string | null;
  portfolioImages: ArtistPortfolioImage[];
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
  id:                string;
  authorName:        string;
  rating:            number;
  body:              string;
  createdAt:         string;
  isVerifiedBooking: boolean;
}

export interface PortfolioImageResponse {
  imageId:             string;
  imageUrl:            string;
  style:               string | null;   // nullable — untagged images are valid
  artistName:          string;
  artistSlug:          string;
  studioName:          string;
  studioSlug:          string;
  averageRating:       number | null;
  reviewCount:         number;
  imageAverageRating:  number | null;
  imageReviewCount:    number;
  distanceKm:          number | null;
  viewCount:           number;
}

export interface PortfolioImageReviewArgs {
  imageId: string;
  rating:  number;
  body:    string;
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

export interface PortfolioFeedArgs {
  lat?:      number;
  lng?:      number;
  radiusKm:  number;
  page:      number;
  pageSize?: number;
  style?:    string;
}

// Attaches the JWT when available so authenticated endpoints (e.g. POST reviews)
// work without triggering the global session-expired redirect for anonymous calls.
const publicBaseQuery = fetchBaseQuery({
  baseUrl: "/api/v1/public/",
  prepareHeaders: (headers, { getState }) => {
    const token = (getState() as RootState).auth?.token;
    if (token) headers.set("Authorization", `Bearer ${token}`);
    return headers;
  },
});

export const publicApi = createApi({
  reducerPath: "publicApi",
  baseQuery: publicBaseQuery,
  tagTypes: [
    "PublicStudio", "PublicArtist", "SharedDesign", "NearbyStudios",
    "StudioReviews", "ArtistReviews", "PortfolioImageReviews", "PortfolioFeed",
  ],
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
    getPortfolioFeed: builder.query<PortfolioImageResponse[], PortfolioFeedArgs>({
      query: ({ lat, lng, radiusKm, page, pageSize = 24, style }) => {
        const params = new URLSearchParams();
        params.set("radiusKm", String(radiusKm));
        params.set("page",     String(page));
        params.set("pageSize", String(pageSize));
        if (lat != null) params.set("lat", String(lat));
        if (lng != null) params.set("lng", String(lng));
        if (style)       params.set("style", style);
        return `portfolio/feed?${params.toString()}`;
      },
      providesTags: ["PortfolioFeed"],
      // Evict immediately on unmount — navigating back to /discover always
      // fetches fresh data; newly uploaded images won't hide behind stale cache.
      keepUnusedDataFor: 0,
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
    getPortfolioImageReviews: builder.query<ReviewResponse[], string>({
      query: (imageId) => `portfolio/${imageId}/reviews`,
      providesTags: (_result, _err, imageId) => [{ type: "PortfolioImageReviews", id: imageId }],
    }),
    createPortfolioImageReview: builder.mutation<void, PortfolioImageReviewArgs>({
      query: ({ imageId, rating, body }) => ({
        url:    `portfolio/${imageId}/reviews`,
        method: "POST",
        body:   { rating, body },
      }),
      invalidatesTags: (_result, _err, { imageId }) => [{ type: "PortfolioImageReviews", id: imageId }],
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
  useGetPortfolioImageReviewsQuery,
  useCreatePortfolioImageReviewMutation,
} = publicApi;
