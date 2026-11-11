import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import type { AuditLogPageResponse, AuditLogQueryParams } from "@/features/platform/platform.types";

export interface RegisterStudioRequest {
  name:          string;
  slug:          string;
  city:          string;
  latitude:      number;
  longitude:     number;
  ownerEmail:    string;
  nipt:          string;
  addressLine1:  string;
  addressLine2?: string;
  postalCode?:   string;
  referralCode?: string;
  countryCode?:  string;
  currency?:     string;
  /** IANA zone id; omitted lets the server keep its default. */
  timezone?:     string;
}

export interface StudioResponse {
  id:                   string;
  name:                 string;
  slug:                 string;
  city:                 string;
  latitude:             number;
  longitude:            number;
  showPlatformBranding: boolean;
  allowBrandingRemoval: boolean;
  allowApiAccess:       boolean;
  trialExpiresAt:       string;
  createdAt:            string;
  isActive:             boolean;
  slugLockedAt:         string | null;
  phoneNumber:          string | null;
  instagramHandle:      string | null;
  nipt:                 string | null;
  isSolo:               boolean;
  isPublished:          boolean;
  timezone:             string;
  subscriptionStatus?:  string | null;
  pastDueSince?:        string | null;
  addressLine1:         string | null;
  addressLine2:         string | null;
  postalCode:           string | null;
  countryCode:          string;
  currency:             string;
  currencyLocked:       boolean;
}

export interface StudioApiKeyStatusResponse {
  hasActiveKey: boolean;
  keyPrefix:    string | null;
  createdAt:    string | null;
  lastUsedAt:   string | null;
}

export interface GenerateApiKeyResponse {
  apiKey:    string;
  keyPrefix: string;
  createdAt: string;
}

export interface WebhookEndpointStatusResponse {
  hasEndpoint:          boolean;
  url:                  string | null;
  isActive:             boolean;
  createdAt:            string | null;
  lastDeliveryAt:       string | null;
  lastDeliverySucceeded: boolean | null;
}

export interface GenerateWebhookSecretResponse {
  url:       string;
  secret:    string;
  createdAt: string;
}

export interface WebhookDeliveryResponse {
  id:                 string;
  eventType:          string;
  responseStatusCode: number | null;
  succeeded:          boolean;
  errorMessage:       string | null;
  attemptedAt:        string;
}

export interface InviteSoloArtistToJoinRequest {
  firstName:        string;
  lastName:         string;
  email:            string;
  specializations?: string[] | null;
  hourlyRate?:      number | null;
}

export interface StudioJoinInviteResponse {
  id:            string;
  invitedEmail:  string;
  status:        string;
  expiresAt:     string;
}

export interface StudioMapItem {
  id: string;
  name: string;
  slug: string;
  latitude: number;
  longitude: number;
  city: string;
}

export interface ReferralCodeResponse {
  id:          string;
  code:        string;
  shareUrl:    string;
  isActive:    boolean;
  isSingleUse: boolean;
  createdAt:   string;
  expiresAt:   string | null;
}

export interface ReferralStatsResponse {
  code:                   string | null;
  redemptionCount:        number;
  discountsApplied:       number;
  referrerRewardsApplied: number;
}

export interface UpdateStudioRequest {
  name:             string;
  city:             string;
  latitude:         number;
  longitude:        number;
  phoneNumber?:     string | null;
  instagramHandle?: string | null;
  nipt?:            string | null;
  timezone?:        string | null;
  addressLine1?:    string | null;
  addressLine2?:    string | null;
  postalCode?:      string | null;
  countryCode?:     string | null;
}

export interface UpdateStudioCurrencyRequest {
  currency: string;
}

export interface StudioClosureResponse {
  id:        string;
  startDate: string;
  endDate:   string;
  reason:    string;
}

export interface AddStudioClosureRequest {
  startDate: string;
  endDate:   string;
  reason:    string;
}

export interface StudioHoursEntry {
  dayOfWeek: number;
  startTime: string;
  endTime:   string;
  isOpen:    boolean;
}

export interface UpsertStudioHoursRequest {
  entries: StudioHoursEntry[];
}

/** E05a (O02): where one setup area stands. Only Hours can be "DefaultUnreviewed" (registration seeds them). */
export type SetupAreaKey = "Artists" | "DepositRules" | "Hours" | "PayModel";
export type SetupAreaState = "NotConfigured" | "DefaultUnreviewed" | "Reviewed";

export interface SetupAreaResponse {
  area:       SetupAreaKey;
  state:      SetupAreaState;
  reviewedAt: string | null;
}

export interface SetupReadinessResponse {
  areas: SetupAreaResponse[];
}

/** GET /studios/me/pay-model (A07a, owner only). Rates are percentages to the artist (60 = 60%). */
export interface StudioPayModel {
  arrangement:       string;
  commissionRate:    number | null;
  flatFeePerSession: number | null;
  /** When the owner saved or confirmed it; null = set for them, not yet reviewed. */
  reviewedAt:        string | null;
  currency:          string;
}

export interface UpdateStudioPayModelRequest {
  arrangement:       string;
  commissionRate:    number | null;
  flatFeePerSession: number | null;
}

export const studiosApi = createApi({
  reducerPath: "studiosApi",
  baseQuery,
  tagTypes: [
    "Studio", "Referral", "StudioClosure", "StudioAuditLog", "StudioHours", "StudioApiKey",
    "WebhookEndpoint", "WebhookDeliveries", "StudioPayModel",
  ],
  endpoints: (builder) => ({
    registerStudio: builder.mutation<StudioResponse, RegisterStudioRequest>({
      query: (body) => ({ url: "studios", method: "POST", body }),
    }),
    getStudioMap: builder.query<StudioMapItem[], void>({
      query: () => "studios/map",
    }),
    getMyStudio: builder.query<StudioResponse, void>({
      query: () => "studios/me",
      providesTags: ["Studio"],
    }),
    updateMyStudio: builder.mutation<StudioResponse, UpdateStudioRequest>({
      query: (body) => ({ url: "studios/me", method: "PUT", body }),
      invalidatesTags: ["Studio"],
    }),
    updateMyStudioCurrency: builder.mutation<StudioResponse, UpdateStudioCurrencyRequest>({
      query: (body) => ({ url: "studios/me/currency", method: "PUT", body }),
      invalidatesTags: ["Studio"],
    }),
    getMyStudioAuditLog: builder.query<AuditLogPageResponse, AuditLogQueryParams | void>({
      query: (params) => ({ url: "studios/me/audit-log", params: params ?? undefined }),
      providesTags: ["StudioAuditLog"],
    }),
    inviteSoloArtistToJoin: builder.mutation<StudioJoinInviteResponse, InviteSoloArtistToJoinRequest>({
      query: (body) => ({ url: "studios/me/join-invites", method: "POST", body }),
    }),
    // Admin: list all studios
    getStudios: builder.query<StudioResponse[], void>({
      query: () => "studios",
      providesTags: ["Studio"],
    }),
    // Admin: get single studio by id
    getStudioById: builder.query<StudioResponse, string>({
      query: (id) => `studios/${id}`,
      providesTags: ["Studio"],
    }),
    updateStudioBranding: builder.mutation<StudioResponse, { id: string; showPlatformBranding: boolean }>({
      query: ({ id, showPlatformBranding }) => ({
        url:    `studios/${id}/branding`,
        method: "PATCH",
        body:   { showPlatformBranding },
      }),
      invalidatesTags: ["Studio"],
    }),
    getApiKeyStatus: builder.query<StudioApiKeyStatusResponse, void>({
      query: () => "studios/me/api-key",
      providesTags: ["StudioApiKey"],
    }),
    generateApiKey: builder.mutation<GenerateApiKeyResponse, void>({
      query: () => ({ url: "studios/me/api-key", method: "POST" }),
      invalidatesTags: ["StudioApiKey"],
    }),
    revokeApiKey: builder.mutation<void, void>({
      query: () => ({ url: "studios/me/api-key", method: "DELETE" }),
      invalidatesTags: ["StudioApiKey"],
    }),
    getWebhookStatus: builder.query<WebhookEndpointStatusResponse, void>({
      query: () => "studios/me/webhook",
      providesTags: ["WebhookEndpoint"],
    }),
    upsertWebhook: builder.mutation<GenerateWebhookSecretResponse, { url: string }>({
      query: (body) => ({ url: "studios/me/webhook", method: "POST", body }),
      invalidatesTags: ["WebhookEndpoint", "WebhookDeliveries"],
    }),
    deleteWebhook: builder.mutation<void, void>({
      query: () => ({ url: "studios/me/webhook", method: "DELETE" }),
      invalidatesTags: ["WebhookEndpoint", "WebhookDeliveries"],
    }),
    sendTestWebhookEvent: builder.mutation<void, void>({
      query: () => ({ url: "studios/me/webhook/test", method: "POST" }),
      invalidatesTags: ["WebhookEndpoint", "WebhookDeliveries"],
    }),
    getWebhookDeliveries: builder.query<WebhookDeliveryResponse[], void>({
      query: () => "studios/me/webhook/deliveries",
      providesTags: ["WebhookDeliveries"],
    }),
    suspendStudio: builder.mutation<void, string>({
      query: (id) => ({ url: `studios/${id}/suspend`, method: "PATCH" }),
      invalidatesTags: ["Studio"],
      async onQueryStarted(id, { dispatch, queryFulfilled }) {
        const patch = dispatch(
          studiosApi.util.updateQueryData("getStudios", undefined, (draft) => {
            const s = draft.find((x) => x.id === id);
            if (s) s.isActive = false;
          }),
        );
        try { await queryFulfilled; } catch { patch.undo(); }
      },
    }),
    unsuspendStudio: builder.mutation<void, string>({
      query: (id) => ({ url: `studios/${id}/unsuspend`, method: "PATCH" }),
      invalidatesTags: ["Studio"],
      async onQueryStarted(id, { dispatch, queryFulfilled }) {
        const patch = dispatch(
          studiosApi.util.updateQueryData("getStudios", undefined, (draft) => {
            const s = draft.find((x) => x.id === id);
            if (s) s.isActive = true;
          }),
        );
        try { await queryFulfilled; } catch { patch.undo(); }
      },
    }),
    getStudioQrCode: builder.query<string, { id: string; format?: "png" | "svg" }>({
      query: ({ id, format = "png" }) => ({
        url:             `studios/${id}/qr`,
        params:          { format },
        responseHandler: async (response) => URL.createObjectURL(await response.blob()),
      }),
      keepUnusedDataFor: 0,
    }),
    generateReferralCode: builder.mutation<ReferralCodeResponse, string>({
      query: (id) => ({ url: `studios/${id}/referral-codes`, method: "POST" }),
      invalidatesTags: ["Referral"],
    }),
    getReferralCode: builder.query<ReferralCodeResponse | null, string>({
      query: (id) => `studios/${id}/referral-codes`,
      providesTags: ["Referral"],
    }),
    getReferralStats: builder.query<ReferralStatsResponse, string>({
      query: (id) => `studios/${id}/referral-stats`,
      providesTags: ["Referral"],
    }),
    updateStudioSlug: builder.mutation<void, { id: string; newSlug: string }>({
      query: ({ id, newSlug }) => ({
        url:    `studios/${id}/slug`,
        method: "PATCH",
        body:   { newSlug },
      }),
      invalidatesTags: ["Studio"],
    }),
    getStudioClosures: builder.query<StudioClosureResponse[], string>({
      query: (id) => `studios/${id}/closures`,
      providesTags: ["StudioClosure"],
    }),
    addStudioClosure: builder.mutation<{ id: string }, { id: string; body: AddStudioClosureRequest }>({
      query: ({ id, body }) => ({
        url:    `studios/${id}/closures`,
        method: "POST",
        body,
      }),
      invalidatesTags: ["StudioClosure"],
    }),
    deleteStudioClosure: builder.mutation<void, { id: string; closureId: string }>({
      query: ({ id, closureId }) => ({
        url:    `studios/${id}/closures/${closureId}`,
        method: "DELETE",
      }),
      invalidatesTags: ["StudioClosure"],
    }),
    getStudioHours: builder.query<StudioHoursEntry[], string>({
      query: (id) => `studios/${id}/hours`,
      providesTags: ["StudioHours"],
    }),
    upsertStudioHours: builder.mutation<void, { id: string; body: UpsertStudioHoursRequest }>({
      query: ({ id, body }) => ({
        url:    `studios/${id}/hours`,
        method: "PUT",
        body,
      }),
      invalidatesTags: ["StudioHours"],
    }),
    // E05a (O02). Tagged StudioHours so saving or confirming hours refreshes it; artists and deposit
    // rules live in other slices, so the checklist also refetches whenever the dashboard mounts.
    getSetupReadiness: builder.query<SetupReadinessResponse, void>({
      query: () => "studios/me/setup-readiness",
      providesTags: ["StudioHours", "StudioPayModel"],
    }),
    confirmStudioHours: builder.mutation<void, void>({
      query: () => ({ url: "studios/me/hours/confirm", method: "POST" }),
      invalidatesTags: ["StudioHours"],
    }),
    // A07a: how the studio's artists are paid by default. Saving is the owner reviewing it.
    getStudioPayModel: builder.query<StudioPayModel, void>({
      query: () => "studios/me/pay-model",
      providesTags: ["StudioPayModel"],
    }),
    updateStudioPayModel: builder.mutation<StudioPayModel, UpdateStudioPayModelRequest>({
      query: (body) => ({ url: "studios/me/pay-model", method: "PUT", body }),
      invalidatesTags: ["StudioPayModel"],
    }),
  }),
});

export const {
  useGetSetupReadinessQuery,
  useConfirmStudioHoursMutation,
  useGetStudioPayModelQuery,
  useUpdateStudioPayModelMutation,
  useRegisterStudioMutation,
  useGetStudioMapQuery,
  useGetMyStudioQuery,
  useUpdateMyStudioMutation,
  useUpdateMyStudioCurrencyMutation,
  useUpdateStudioBrandingMutation,
  useGetApiKeyStatusQuery,
  useGenerateApiKeyMutation,
  useRevokeApiKeyMutation,
  useGetWebhookStatusQuery,
  useUpsertWebhookMutation,
  useDeleteWebhookMutation,
  useSendTestWebhookEventMutation,
  useGetWebhookDeliveriesQuery,
  useGetStudiosQuery,
  useGetStudioByIdQuery,
  useSuspendStudioMutation,
  useUnsuspendStudioMutation,
  useGetStudioQrCodeQuery,
  useLazyGetStudioQrCodeQuery,
  useGenerateReferralCodeMutation,
  useGetReferralCodeQuery,
  useGetReferralStatsQuery,
  useUpdateStudioSlugMutation,
  useGetStudioClosuresQuery,
  useAddStudioClosureMutation,
  useDeleteStudioClosureMutation,
  useGetStudioHoursQuery,
  useUpsertStudioHoursMutation,
  useGetMyStudioAuditLogQuery,
  useInviteSoloArtistToJoinMutation,
} = studiosApi;
