import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { RootState } from "@/app/store";
import type { FileConductReportArgs, ReportableAppointment } from "@/features/conduct-reports/conductReports.types";

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

export interface PublicSocialLinkResponse {
  platform:   string;
  handle:     string;
  isVerified: boolean;
  profileUrl: string;
}

export interface PublicStudioResponse {
  studioId:       string;
  name:           string;
  slug:           string;
  city:           string;
  latitude:       number;
  longitude:      number;
  description:    string | null;
  coverImageUrl:  string | null;
  phoneNumber:    string | null;
  averageRating:  number | null;
  reviewCount:    number;
  galleryImages:  string[];
  artists:        PublicArtistSummary[];
  showBookingCta: boolean;
  socialLinks:    PublicSocialLinkResponse[];
}

export interface ArtistPortfolioImage {
  imageId:  string;
  imageUrl: string;
  style:    string | null;
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
  socialLinks:     PublicSocialLinkResponse[];
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
  ownerResponse:     string | null;
  ownerResponseAt:   string | null;
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
  slug:          string;
  appointmentId: string;
  rating:        number;
  body:          string;
}

export interface ReviewableAppointmentResponse {
  id:              string;
  date:            string;
  durationMinutes: number;
}

export interface ArtistInstagramPostResponse {
  id:               string;
  instagramMediaId: string;
  mediaUrl:         string | null;
  thumbnailUrl:     string | null;
  caption:          string | null;
  mediaType:        string;
  postedAt:         string;
  isVisible:        boolean;
}

export interface PortfolioFeedArgs {
  lat?:      number;
  lng?:      number;
  radiusKm:  number;
  page:      number;
  pageSize?: number;
  style?:    string;
  search?:   string;
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
    "StudioReportableAppointments", "ArtistReportableAppointments",
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
      // Evict immediately on unmount — a token that expires between visits must never
      // be served from cache showing an image that the backend would now 404 on.
      keepUnusedDataFor: 0,
    }),
    getNearbyStudios: builder.query<NearbyStudioResponse[], NearbyStudiosArgs>({
      query: ({ lat, lng, radiusKm }) =>
        `studios/nearby?lat=${lat}&lng=${lng}&radiusKm=${radiusKm}`,
      providesTags: ["NearbyStudios"],
    }),
    getPortfolioFeed: builder.query<PortfolioImageResponse[], PortfolioFeedArgs>({
      query: ({ lat, lng, radiusKm, page, pageSize = 24, style, search }) => {
        const params = new URLSearchParams();
        params.set("radiusKm", String(radiusKm));
        params.set("page",     String(page));
        params.set("pageSize", String(pageSize));
        if (lat != null) params.set("lat", String(lat));
        if (lng != null) params.set("lng", String(lng));
        if (style)        params.set("style",  style);
        if (search)       params.set("search", search);
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
    // Which of the caller's completed appointments at this studio/artist don't yet
    // have a review — powers the "which visit are you reviewing?" picker. Shares the
    // StudioReviews/ArtistReviews tag so submitting a review refreshes both lists.
    getReviewableStudioAppointments: builder.query<ReviewableAppointmentResponse[], string>({
      query: (slug) => `studios/${slug}/reviews/eligible-appointments`,
      providesTags: (_result, _err, slug) => [{ type: "StudioReviews", id: slug }],
    }),
    getReviewableArtistAppointments: builder.query<ReviewableAppointmentResponse[], string>({
      query: (slug) => `artists/${slug}/reviews/eligible-appointments`,
      providesTags: (_result, _err, slug) => [{ type: "ArtistReviews", id: slug }],
    }),
    createStudioReview: builder.mutation<void, CreateReviewArgs>({
      query: ({ slug, appointmentId, rating, body }) => ({
        url:    `studios/${slug}/reviews`,
        method: "POST",
        body:   { appointmentId, rating, body },
      }),
      invalidatesTags: (_result, _err, { slug }) => [{ type: "StudioReviews", id: slug }],
    }),
    createArtistReview: builder.mutation<void, CreateReviewArgs>({
      query: ({ slug, appointmentId, rating, body }) => ({
        url:    `artists/${slug}/reviews`,
        method: "POST",
        body:   { appointmentId, rating, body },
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
    getArtistInstagramPosts: builder.query<ArtistInstagramPostResponse[], string>({
      query: (slug) => `artists/${slug}/instagram-posts`,
    }),
    // Every real appointment the caller has with this studio/artist, regardless of status —
    // deliberately NOT restricted to completed/unreported like the reviews equivalent above
    // (see FileArtistConductReportCommand's doc comment for why).
    getReportableStudioAppointments: builder.query<ReportableAppointment[], string>({
      query: (slug) => `studios/${slug}/reports/reportable-appointments`,
      providesTags: (_result, _err, slug) => [{ type: "StudioReportableAppointments", id: slug }],
    }),
    getReportableArtistAppointments: builder.query<ReportableAppointment[], string>({
      query: (slug) => `artists/${slug}/reports/reportable-appointments`,
      providesTags: (_result, _err, slug) => [{ type: "ArtistReportableAppointments", id: slug }],
    }),
    fileStudioConductReport: builder.mutation<void, { slug: string; body: FileConductReportArgs }>({
      query: ({ slug, body }) => ({ url: `studios/${slug}/reports`, method: "POST", body }),
    }),
    fileArtistConductReport: builder.mutation<void, { slug: string; body: FileConductReportArgs }>({
      query: ({ slug, body }) => ({ url: `artists/${slug}/reports`, method: "POST", body }),
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
  useGetReviewableStudioAppointmentsQuery,
  useGetReviewableArtistAppointmentsQuery,
  useCreateStudioReviewMutation,
  useCreateArtistReviewMutation,
  useGetPortfolioImageReviewsQuery,
  useCreatePortfolioImageReviewMutation,
  useGetArtistInstagramPostsQuery,
  useGetReportableStudioAppointmentsQuery,
  useGetReportableArtistAppointmentsQuery,
  useFileStudioConductReportMutation,
  useFileArtistConductReportMutation,
} = publicApi;
