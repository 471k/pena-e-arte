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

export interface InstagramConnectionStatus {
  isConnected:  boolean;
  username:     string | null;
  lastSyncedAt: string | null;
  postCount:    number;
}

export interface InstagramPostItem {
  id:               string;
  instagramMediaId: string;
  mediaUrl:         string | null;
  thumbnailUrl:     string | null;
  caption:          string | null;
  mediaType:        string;
  postedAt:         string;
  isVisible:        boolean;
}

export interface ConnectInstagramResponse {
  authUrl: string;
}

export interface ArtistScheduleEntry {
  dayOfWeek:   number; // 0 = Sunday .. 6 = Saturday, matches .NET DayOfWeek
  startTime:   string; // "HH:mm:ss"
  endTime:     string; // "HH:mm:ss"
  isAvailable: boolean;
}

export interface ArtistTimeOffEntry {
  id:        string;
  startDate: string;
  endDate:   string;
  reason:    string;
}

export interface ArtistAvailabilityResponse {
  schedule: ArtistScheduleEntry[];
  timeOff:  ArtistTimeOffEntry[];
}

export interface AddArtistTimeOffRequest {
  startDate: string;
  endDate:   string;
  reason:    string;
}

export const artistsApi = createApi({
  reducerPath: "artistsApi",
  baseQuery,
  tagTypes: ["Artist", "ArtistSchedule"],
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
    getInstagramConnectUrl: builder.query<ConnectInstagramResponse, string>({
      query: (artistId) => `artists/${artistId}/instagram/connect-url`,
    }),
    getInstagramStatus: builder.query<InstagramConnectionStatus, string>({
      query: (artistId) => `artists/${artistId}/instagram/status`,
      providesTags: (_result, _err, artistId) => [{ type: "Artist", id: `${artistId}-instagram` }],
    }),
    getInstagramPosts: builder.query<InstagramPostItem[], { artistId: string; page?: number }>({
      query: ({ artistId, page = 1 }) => `artists/${artistId}/instagram/posts?page=${page}`,
      providesTags: (_result, _err, { artistId }) => [{ type: "Artist", id: `${artistId}-instagram-posts` }],
    }),
    toggleInstagramPostVisibility: builder.mutation<
      void,
      { artistId: string; postId: string; isVisible: boolean }
    >({
      query: ({ artistId, postId, isVisible }) => ({
        url:    `artists/${artistId}/instagram/posts/${postId}/visibility`,
        method: "PUT",
        body:   { isVisible },
      }),
      invalidatesTags: (_result, _err, { artistId }) => [
        { type: "Artist", id: `${artistId}-instagram-posts` },
      ],
    }),
    disconnectInstagram: builder.mutation<void, string>({
      query: (artistId) => ({
        url:    `artists/${artistId}/instagram/disconnect`,
        method: "DELETE",
      }),
      invalidatesTags: (_result, _err, artistId) => [{ type: "Artist", id: `${artistId}-instagram` }],
    }),
    getArtistSchedule: builder.query<ArtistAvailabilityResponse, string>({
      query: (artistId) => `artists/${artistId}/schedule`,
      providesTags: (_result, _err, artistId) => [{ type: "ArtistSchedule", id: artistId }],
    }),
    upsertArtistSchedule: builder.mutation<void, { artistId: string; entries: ArtistScheduleEntry[] }>({
      query: ({ artistId, entries }) => ({
        url:    `artists/${artistId}/schedule`,
        method: "PUT",
        body:   { entries },
      }),
      invalidatesTags: (_result, _err, { artistId }) => [{ type: "ArtistSchedule", id: artistId }],
    }),
    addArtistTimeOff: builder.mutation<{ id: string }, { artistId: string; body: AddArtistTimeOffRequest }>({
      query: ({ artistId, body }) => ({
        url:    `artists/${artistId}/time-off`,
        method: "POST",
        body,
      }),
      invalidatesTags: (_result, _err, { artistId }) => [{ type: "ArtistSchedule", id: artistId }],
    }),
    deleteArtistTimeOff: builder.mutation<void, { artistId: string; timeOffId: string }>({
      query: ({ artistId, timeOffId }) => ({
        url:    `artists/${artistId}/time-off/${timeOffId}`,
        method: "DELETE",
      }),
      invalidatesTags: (_result, _err, { artistId }) => [{ type: "ArtistSchedule", id: artistId }],
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
  useLazyGetInstagramConnectUrlQuery,
  useGetInstagramStatusQuery,
  useGetInstagramPostsQuery,
  useToggleInstagramPostVisibilityMutation,
  useDisconnectInstagramMutation,
  useGetArtistScheduleQuery,
  useUpsertArtistScheduleMutation,
  useAddArtistTimeOffMutation,
  useDeleteArtistTimeOffMutation,
} = artistsApi;
