export const AppointmentStatus = {
  Pending:   "Pending",
  Confirmed: "Confirmed",
  Cancelled: "Cancelled",
  Completed: "Completed",
  NoShow:    "NoShow",
} as const;
export type AppointmentStatus = (typeof AppointmentStatus)[keyof typeof AppointmentStatus];

export const DepositStatus = {
  Pending:   "Pending",
  Paid:      "Paid",
  Forfeited: "Forfeited",
  Refunded:  "Refunded",
} as const;
export type DepositStatus = (typeof DepositStatus)[keyof typeof DepositStatus];

export const AppointmentAttachmentCategory = {
  AreaPhoto: "AreaPhoto",
  Reference: "Reference",
} as const;
export type AppointmentAttachmentCategory =
  (typeof AppointmentAttachmentCategory)[keyof typeof AppointmentAttachmentCategory];

export interface AppointmentAttachmentResponse {
  url:      string;
  category: AppointmentAttachmentCategory;
}

export interface AppointmentImageRequest {
  url:      string;
  category: AppointmentAttachmentCategory;
}

export const ReferralSource = {
  Instagram:        "Instagram",
  TikTok:           "TikTok",
  YouTube:          "YouTube",
  FriendsAndFamily: "FriendsAndFamily",
  Other:            "Other",
} as const;
export type ReferralSource = (typeof ReferralSource)[keyof typeof ReferralSource];

export interface AppointmentResponse {
  id:                 string;
  studioId:           string;
  artistId:           string | null;
  clientId:           string;
  date:               string;
  endDate:            string;
  durationMinutes:    number;
  status:             AppointmentStatus;
  depositStatus:      DepositStatus;
  depositAmount:      number;
  notes:              string | null;
  createdAt:          string;
  cancellationReason?: string | null;
  aftercareSentAt?:    string | null;
  clientName?:         string | null;
  /** @deprecated Use `attachments` (category-split). Kept for one release during the frontend migration. */
  imageUrls?:          string[];
  artistName?:         string | null;
  clientUserId?:       string | null;
  tattooDescription?:         string | null;
  safetyNotes?:               string | null;
  desiredPlacementLocations?: string[] | null;
  referralSource?:            string | null;
  referralSourceOther?:       string | null;
  attachments?:               AppointmentAttachmentResponse[] | null;
}

export interface CreateAppointmentRequest {
  artistId:        string | null;
  clientId:        string;
  date:            string;
  durationMinutes: number;
  depositRuleId:   string | null;
  notes:           string | null;
  tattooDescription:          string;
  safetyNotes?:               string | null;
  desiredPlacementLocations?: string[];
  referralSource?:            string | null;
  referralSourceOther?:       string | null;
  images?:                    AppointmentImageRequest[];
}

export interface AssignAppointmentArtistRequest {
  artistId: string;
}

export interface RescheduleAppointmentRequest {
  newDate:            string;
  newDurationMinutes: number;
  notes:              string | null;
}

export interface GetAppointmentsParams {
  from?:     string;
  to?:       string;
  artistId?: string;
}

export interface SlotAvailabilityResponse {
  available: boolean;
  reason:    string | null;
}

export interface CheckSlotAvailabilityParams {
  artistId?:       string;
  date:            string;
  durationMinutes: number;
}
