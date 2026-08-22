import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";

export type SocialSubjectType = "Artist" | "Studio";

// Keep in sync with Pena_e_Arte.Domain.Enums.SocialPlatform.
export type SocialPlatform = "Instagram" | "TikTok" | "Facebook" | "X" | "YouTube";

export const ALL_SOCIAL_PLATFORMS: readonly SocialPlatform[] =
  ["Instagram", "TikTok", "Facebook", "X", "YouTube"];

export interface SocialLinkStatus {
  platform:               SocialPlatform;
  handle:                 string | null;
  isVerified:             boolean;
  verifiedAt:             string | null;
  verificationMethod:     "OAuthConnect" | "ManualBioCode" | null;
  isOAuthConfigured:      boolean;
  isManualCheckSupported: boolean;
  hasPendingCode:         boolean;
  pendingCodeExpiresAt:   string | null;
}

export interface SocialConnectUrlResponse {
  authUrl: string;
}

export interface SocialVerificationCodeResponse {
  code:      string;
  expiresAt: string;
}

export interface SocialVerifyResult {
  verified:      boolean;
  failureReason: string | null;
}

interface SocialSubjectArg {
  subjectType: SocialSubjectType;
  subjectId:   string;
}

function subjectBasePath({ subjectType, subjectId }: SocialSubjectArg): string {
  const segment = subjectType === "Studio" ? "studios" : "artists";
  return `${segment}/${subjectId}/social`;
}

function subjectTagId({ subjectType, subjectId }: SocialSubjectArg): string {
  return `${subjectType}-${subjectId}`;
}

export const socialApi = createApi({
  reducerPath: "socialApi",
  baseQuery,
  tagTypes: ["SocialLinks"],
  endpoints: (builder) => ({
    getSocialLinks: builder.query<SocialLinkStatus[], SocialSubjectArg>({
      query: (arg) => subjectBasePath(arg),
      providesTags: (_result, _err, arg) => [{ type: "SocialLinks", id: subjectTagId(arg) }],
    }),
    getSocialConnectUrl: builder.query<SocialConnectUrlResponse, SocialSubjectArg & { platform: SocialPlatform }>({
      query: ({ platform, ...arg }) => `${subjectBasePath(arg)}/${platform}/connect-url`,
    }),
    updateSocialHandle: builder.mutation<void, SocialSubjectArg & { platform: SocialPlatform; handle: string }>({
      query: ({ platform, handle, ...arg }) => ({
        url:    `${subjectBasePath(arg)}/${platform}/handle`,
        method: "PUT",
        body:   { handle },
      }),
      invalidatesTags: (_result, _err, arg) => [{ type: "SocialLinks", id: subjectTagId(arg) }],
    }),
    requestSocialVerificationCode: builder.mutation<
      SocialVerificationCodeResponse, SocialSubjectArg & { platform: SocialPlatform }
    >({
      query: ({ platform, ...arg }) => ({
        url:    `${subjectBasePath(arg)}/${platform}/request-code`,
        method: "POST",
      }),
      invalidatesTags: (_result, _err, arg) => [{ type: "SocialLinks", id: subjectTagId(arg) }],
    }),
    verifySocialBioCode: builder.mutation<SocialVerifyResult, SocialSubjectArg & { platform: SocialPlatform }>({
      query: ({ platform, ...arg }) => ({
        url:    `${subjectBasePath(arg)}/${platform}/verify-code`,
        method: "POST",
      }),
      invalidatesTags: (_result, _err, arg) => [{ type: "SocialLinks", id: subjectTagId(arg) }],
    }),
    disconnectSocialAccount: builder.mutation<void, SocialSubjectArg & { platform: SocialPlatform }>({
      query: ({ platform, ...arg }) => ({
        url:    `${subjectBasePath(arg)}/${platform}/disconnect`,
        method: "DELETE",
      }),
      invalidatesTags: (_result, _err, arg) => [{ type: "SocialLinks", id: subjectTagId(arg) }],
    }),
  }),
});

export const {
  useGetSocialLinksQuery,
  useLazyGetSocialConnectUrlQuery,
  useUpdateSocialHandleMutation,
  useRequestSocialVerificationCodeMutation,
  useVerifySocialBioCodeMutation,
  useDisconnectSocialAccountMutation,
} = socialApi;
