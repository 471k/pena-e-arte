import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";

interface LoginRequest {
  email:    string;
  password: string;
}

export interface AuthResponse {
  accessToken:  string;
  refreshToken: string;
  tokenType:    string;
}

interface RegisterUserRequest {
  email:      string;
  password:   string;
  role:       string;
  studioId?:  string;
  firstName?: string;
}

export interface RegisterSoloArtistRequest {
  email:     string;
  password:  string;
  firstName: string;
  lastName:  string;
}

interface OAuthLoginRequest {
  provider: string;
  idToken:  string;
}

interface OAuthRegisterRequest {
  provider: string;
  idToken:  string;
  role:     string;
  studioId: string;
}

interface ResetPasswordRequest {
  email:       string;
  token:       string;
  newPassword: string;
}

interface RefreshTokenRequest {
  refreshToken: string;
}

interface ChangePasswordRequest {
  currentPassword: string;
  newPassword:     string;
}

interface RequestChangeEmailRequest {
  currentPassword: string;
  newEmail:        string;
}

interface SwitchStudioRequest {
  studioId: string;
}

export interface SwitchStudioResponse {
  accessToken:    string;
  refreshToken:   string;
  isNewMembership: boolean;
  tokenType:      string;
}

export interface MyStudioResponse {
  studioId:       string;
  name:           string;
  slug:           string;
  city:           string;
  coverImageUrl:  string | null;
  isStudioActive: boolean;
}

export interface LeaveStudioResponse {
  isLeavingActiveTenant: boolean;
}

export interface ClientNotificationPreferenceItem {
  type:      string;
  channel:   "Email" | "Sms";
  isEnabled: boolean;
}

export interface ClientNotificationPreferencesResponse {
  preferences: ClientNotificationPreferenceItem[];
}

export interface MyStudioJoinInviteResponse {
  id:         string;
  studioId:   string;
  studioName: string;
  studioSlug: string;
  studioCity: string;
  expiresAt:  string;
}

export const authApi = createApi({
  reducerPath: "authApi",
  baseQuery,
  tagTypes: ["MyStudios", "ClientStudioNotificationPreferences", "JoinInvites"],
  endpoints: (builder) => ({
    login: builder.mutation<AuthResponse, LoginRequest>({
      query: (body) => ({ url: "auth/login", method: "POST", body }),
    }),
    registerUser: builder.mutation<void, RegisterUserRequest>({
      query: (body) => ({ url: "auth/register", method: "POST", body }),
    }),
    registerSoloArtist: builder.mutation<void, RegisterSoloArtistRequest>({
      query: (body) => ({ url: "auth/register/solo-artist", method: "POST", body }),
    }),
    oauthLogin: builder.mutation<AuthResponse, OAuthLoginRequest>({
      query: (body) => ({ url: "auth/oauth/login", method: "POST", body }),
    }),
    oauthRegister: builder.mutation<void, OAuthRegisterRequest>({
      query: (body) => ({ url: "auth/oauth/register", method: "POST", body }),
    }),
    requestPasswordReset: builder.mutation<{ message: string }, string>({
      query: (email) => ({ url: "auth/forgot-password", method: "POST", body: { email } }),
    }),
    resetPassword: builder.mutation<void, ResetPasswordRequest>({
      query: (body) => ({ url: "auth/reset-password", method: "POST", body }),
    }),
    refreshToken: builder.mutation<AuthResponse, RefreshTokenRequest>({
      query: (body) => ({ url: "auth/refresh", method: "POST", body }),
    }),
    changePassword: builder.mutation<void, ChangePasswordRequest>({
      query: (body) => ({ url: "auth/change-password", method: "PATCH", body }),
    }),
    requestChangeEmail: builder.mutation<void, RequestChangeEmailRequest>({
      query: (body) => ({ url: "auth/change-email", method: "POST", body }),
    }),
    resendVerificationEmail: builder.mutation<void, void>({
      query: () => ({ url: "auth/resend-verification", method: "POST" }),
    }),
    switchStudio: builder.mutation<SwitchStudioResponse, SwitchStudioRequest>({
      query: (body) => ({ url: "auth/switch-studio", method: "POST", body }),
      invalidatesTags: ["MyStudios"],
    }),
    getMyStudios: builder.query<MyStudioResponse[], void>({
      query: () => "auth/my-studios",
      providesTags: ["MyStudios"],
    }),
    leaveStudio: builder.mutation<LeaveStudioResponse, { studioId: string }>({
      query: ({ studioId }) => ({
        url:    `auth/my-studios/${studioId}`,
        method: "DELETE",
      }),
      invalidatesTags: ["MyStudios"],
    }),
    getClientStudioNotificationPreferences: builder.query<
      ClientNotificationPreferencesResponse,
      { studioId: string }
    >({
      query: ({ studioId }) => `auth/my-studios/${studioId}/notification-preferences`,
      providesTags: (_result, _err, { studioId }) => [
        { type: "ClientStudioNotificationPreferences", id: studioId },
      ],
    }),
    updateClientStudioNotificationPreferences: builder.mutation<
      void,
      { studioId: string; preferences: ClientNotificationPreferenceItem[] }
    >({
      query: ({ studioId, preferences }) => ({
        url:    `auth/my-studios/${studioId}/notification-preferences`,
        method: "PUT",
        body:   { preferences },
      }),
      invalidatesTags: (_result, _err, { studioId }) => [
        { type: "ClientStudioNotificationPreferences", id: studioId },
      ],
    }),
    getMyJoinInvites: builder.query<MyStudioJoinInviteResponse[], void>({
      query: () => "auth/join-invites",
      providesTags: ["JoinInvites"],
    }),
    acceptJoinInvite: builder.mutation<AuthResponse, { inviteId: string }>({
      query: ({ inviteId }) => ({ url: `auth/join-invites/${inviteId}/accept`, method: "POST" }),
      invalidatesTags: ["JoinInvites"],
    }),
    declineJoinInvite: builder.mutation<void, { inviteId: string }>({
      query: ({ inviteId }) => ({ url: `auth/join-invites/${inviteId}/decline`, method: "POST" }),
      invalidatesTags: ["JoinInvites"],
    }),
  }),
});

export const {
  useLoginMutation,
  useRegisterUserMutation,
  useRegisterSoloArtistMutation,
  useOauthLoginMutation,
  useOauthRegisterMutation,
  useRequestPasswordResetMutation,
  useResetPasswordMutation,
  useRefreshTokenMutation,
  useChangePasswordMutation,
  useRequestChangeEmailMutation,
  useResendVerificationEmailMutation,
  useSwitchStudioMutation,
  useGetMyStudiosQuery,
  useLeaveStudioMutation,
  useGetClientStudioNotificationPreferencesQuery,
  useUpdateClientStudioNotificationPreferencesMutation,
  useGetMyJoinInvitesQuery,
  useAcceptJoinInviteMutation,
  useDeclineJoinInviteMutation,
} = authApi;
