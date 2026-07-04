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

interface SwitchStudioRequest {
  studioId: string;
}

export interface SwitchStudioResponse {
  accessToken:    string;
  refreshToken:   string;
  isNewMembership: boolean;
  tokenType:      string;
}

export const authApi = createApi({
  reducerPath: "authApi",
  baseQuery,
  endpoints: (builder) => ({
    login: builder.mutation<AuthResponse, LoginRequest>({
      query: (body) => ({ url: "auth/login", method: "POST", body }),
    }),
    registerUser: builder.mutation<void, RegisterUserRequest>({
      query: (body) => ({ url: "auth/register", method: "POST", body }),
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
    resendVerificationEmail: builder.mutation<void, void>({
      query: () => ({ url: "auth/resend-verification", method: "POST" }),
    }),
    switchStudio: builder.mutation<SwitchStudioResponse, SwitchStudioRequest>({
      query: (body) => ({ url: "auth/switch-studio", method: "POST", body }),
    }),
  }),
});

export const {
  useLoginMutation,
  useRegisterUserMutation,
  useOauthLoginMutation,
  useOauthRegisterMutation,
  useRequestPasswordResetMutation,
  useResetPasswordMutation,
  useRefreshTokenMutation,
  useChangePasswordMutation,
  useResendVerificationEmailMutation,
  useSwitchStudioMutation,
} = authApi;
