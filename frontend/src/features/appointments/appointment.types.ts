import type { PaymentMethod, PaymentStatus } from "@/features/payments/payment.types";

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
  PrePaid:   "PrePaid",
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
  style?:                     string | null;
  safetyNotes?:               string | null;
  desiredPlacementLocations?: string[] | null;
  referralSource?:            string | null;
  referralSourceOther?:       string | null;
  attachments?:               AppointmentAttachmentResponse[] | null;
  promoCodeApplied?:          boolean;
  serviceId?:                 string | null;
  serviceName?:               string | null;
  /** Client self-service policy, computed by the server. Only on the client's own reads
   *  (GET /appointments/mine and GET /appointments/mine/{id}). */
  selfService?:               AppointmentSelfServiceResponse | null;
  /** How many of the service's preparation requirements are met (DEC03), derived by the server.
   *  On the client's own reads and the staff list (GET /appointments). `required === 0` means none
   *  are configured: show no checklist. */
  readiness?:                 AppointmentReadinessSummary | null;
}

export interface AppointmentReadinessSummary {
  required:  number;
  satisfied: number;
}

/** Stable keys, in the server's priority order. */
export const ReadinessKey = {
  DepositPaid:    "DepositPaid",
  IntakeForm:     "IntakeForm",
  ConsentForm:    "ConsentForm",
  ReferenceImage: "ReferenceImage",
} as const;
export type ReadinessKey = (typeof ReadinessKey)[keyof typeof ReadinessKey];

/**
 * One preparation requirement the appointment's service turns on, with whether it is met — derived by
 * the server from existing consent / intake / attachment / deposit data and never stored. Readiness
 * never blocks the booking.
 */
export interface AppointmentReadinessItem {
  key:         ReadinessKey;
  label:       string;
  actionLabel: string;
  required:    boolean;
  satisfied:   boolean;
  /** The client can do it in the app now. */
  available:   boolean;
  /** An existing app route, or null when it's done on the appointment page itself (the deposit). */
  destination: string | null;
  /** Shown with an outstanding item the client can't act on in the app. */
  note:        string | null;
}

/** GET /appointments/mine/{id} — one of the caller's own appointments, for the client appointment page. */
export interface ClientAppointmentDetailResponse {
  appointment: AppointmentResponse;
  /** Priority order. Empty when the service configures no requirements. */
  readiness:   AppointmentReadinessItem[];
  contact:     ClientAppointmentContact;
  /** Null unless Confirmed — an unconfirmed request never receives the studio's address. */
  visit?:      ClientAppointmentVisit | null;
  /** Null unless Completed and the studio's aftercare email actually went out. */
  aftercare?:  ClientAppointmentAftercare | null;
}

/** DEC11. `addressComplete` false: the studio hasn't entered a street and city; every field is null. */
export interface ClientAppointmentVisit {
  addressComplete: boolean;
  addressLine1:    string | null;
  addressLine2:    string | null;
  city:            string | null;
  postalCode:      string | null;
}

/** DEC11. The steps the studio's aftercare email sent — the server's one copy, never a frontend one. */
export interface ClientAppointmentAftercare {
  sentAt: string;
  steps:  string[];
}

/**
 * Who a message from the appointment page reaches (DEC06): the appointment's artist when they can sign
 * in, otherwise the studio owner — the same rule the server checks when the conversation is created.
 * `recipientUserId` null means nobody can be messaged: show the studio's details, never a compose box.
 */
export interface ClientAppointmentContact {
  recipientUserId: string | null;
  recipientRole:   "artist" | "owner" | null;
  recipientName:   string | null;
  studioName:      string;
  studioPhone:     string | null;
  studioInstagram: string | null;
}

/**
 * The notice window that applies to this appointment and whether the client is still inside it
 * (i.e. still has at least that much notice). Computed by the server (ClientCancellationPolicy) —
 * the frontend never recomputes it: a frontend copy drifted once (G-3/G-17, October 2026).
 */
export interface AppointmentSelfServiceResponse {
  noticeWindowHours:    number;
  isWithinNoticeWindow: boolean;
}

/** What the server says cancelling would do with the client's money (CancellationClientOutcome). */
export const CancellationOutcome = {
  NothingCollected:  "NothingCollected",
  FullyRefunded:     "FullyRefunded",
  PartiallyRefunded: "PartiallyRefunded",
  DepositKept:       "DepositKept",
  SettledWithStudio: "SettledWithStudio",
  Unknown:           "Unknown",
} as const;
export type CancellationOutcome = (typeof CancellationOutcome)[keyof typeof CancellationOutcome];

/**
 * GET /appointments/{id}/cancellation-preview. A projection worked out by the server with the same
 * function the cancellation itself uses; the server decides again when the cancellation is
 * committed, and that result is what counts.
 */
export interface CancellationPreviewResponse {
  appointmentId:        string;
  noticeWindowHours:    number;
  isWithinNoticeWindow: boolean;
  noticeWindowApplies:  boolean;
  refundPercent:        number;
  depositAmount:        number;
  currency:             string;
  refundAmount:         number;
  keptAmount:           number;
  outcome:              CancellationOutcome;
  isProjection:         boolean;
}

export interface CreateAppointmentRequest {
  artistId:        string | null;
  clientId:        string;
  date:            string;
  durationMinutes: number;
  depositRuleId:   string | null;
  notes:           string | null;
  tattooDescription:          string;
  style?:                     string | null;
  safetyNotes?:               string | null;
  desiredPlacementLocations?: string[];
  referralSource?:            string | null;
  referralSourceOther?:       string | null;
  images?:                    AppointmentImageRequest[];
  promoCode?:                 string | null;
  // Reward-bearing client referral (P1 #4) — distinct from referralSource ("how did you
  // hear about us"). referralCode redeems someone else's ClientReferralCode; referralRewardId
  // redeems the caller's own earned credit.
  referralCode?:               string | null;
  referralRewardId?:           string | null;
  // When present, a confirmed PackagePurchase with sessionsRemaining > 0 covers this
  // booking's deposit entirely — mutually exclusive with promoCode/referralCode/
  // referralRewardId in practice, see CreateAppointmentCommand.
  packagePurchaseId?:         string | null;
  // When present, DurationMinutes/DepositAmount are re-derived server-side from the
  // service (never trusted from this request once set) — see CreateAppointmentCommand.
  serviceId?:                 string | null;
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
  /** Only the caller's own assigned sessions, whatever their role (the artist's Today view). */
  assignedToMe?: boolean;
}

/**
 * One preparation requirement for the artist (E04a), from the same server derivation as the client's
 * checklist but worded about the client: where it stands, never an instruction to do it.
 */
export interface AppointmentPreparationItem {
  key:       ReadinessKey;
  label:     string;
  satisfied: boolean;
  /** Third person, e.g. "Signed" or "Not signed yet". */
  state:     string;
  /** Outstanding and upcoming only: what the client can still do, so the artist knows what to ask for. */
  followUp:  string | null;
}

/** The deposit payment's outcome fields, as describeDeposit reads them. No splits or other payment detail. */
export interface AppointmentPreparationDeposit {
  amount:            number;
  status:            PaymentStatus;
  method:            PaymentMethod;
  paidAt:            string | null;
  currency:          string;
  refundedAmount:    number | null;
  appointmentStatus: string;
  referenceCurrency: string | null;
  referenceRate:     number | null;
}

/** GET /appointments/{id}/preparation — staff only; 404 for an artist's session that isn't theirs. */
export interface AppointmentPreparationResponse {
  appointmentId: string;
  /** Priority order. Empty when the service configures no requirements. */
  items:         AppointmentPreparationItem[];
  /** Null when there is no payment row. */
  deposit:       AppointmentPreparationDeposit | null;
}

export interface SlotAvailabilityResponse {
  available: boolean;
  reason:    string | null;
}

export interface CheckSlotAvailabilityParams {
  artistId?:       string;
  date:            string;
  durationMinutes: number;
  /** The appointment being rescheduled, so it doesn't block its own move. Only one the caller may move. */
  excludeAppointmentId?: string;
}

/**
 * GET /appointments/{id}/communication (E04b, A08). Only what the data proves: not sent, sent (handed to
 * our provider or posted in the conversation) and failed. Never delivery or reading.
 */
export interface AppointmentCommunicationResponse {
  appointmentId: string;
  /** Null unless the appointment has been rescheduled. */
  reschedule:    RescheduleCommunication | null;
  /** Null unless the appointment is Completed. */
  aftercare:     AftercareCommunication | null;
}

export interface RescheduleCommunication {
  state:         "NotSent" | "Sent";
  rescheduledAt: string;
  /** The first staff message about this appointment since it was moved. */
  messagedAt:    string | null;
}

export interface AftercareCommunication {
  state:    "NotSent" | "Sent" | "Failed" | "NotRecorded";
  /** When the aftercare step ran, or null. */
  ranAt:    string | null;
  channels: CommunicationChannel[];
}

export interface CommunicationChannel {
  channel: "Email" | "Sms";
  /** Our provider accepted it. Not a delivery receipt. */
  sent:    boolean;
  at:      string | null;
}

/** What a no-show does to the deposit (E04b, A04), decided by the server's NoShowOutcome. */
export const NoShowOutcome = {
  NothingCollected:           "NothingCollected",
  ChargedDepositKept:         "ChargedDepositKept",
  CashDepositKept:            "CashDepositKept",
  HeldDepositCaptured:        "HeldDepositCaptured",
  DeclaredCashRecordedAsPaid: "DeclaredCashRecordedAsPaid",
  Unknown:                    "Unknown",
} as const;
export type NoShowOutcome = (typeof NoShowOutcome)[keyof typeof NoShowOutcome];

/** GET /appointments/{id}/no-show-preview — a projection; the commit decides again. */
export interface NoShowPreviewResponse {
  appointmentId: string;
  canMarkNoShow: boolean;
  /** The commit's own refusal when it would refuse; null otherwise. */
  message:       string | null;
  outcome:       NoShowOutcome;
  /** The money concerned; 0 when nothing was collected. */
  amount:        number;
  currency:      string;
  isProjection:  boolean;
}

/** PATCH /appointments/{id}/no-show — what the commit actually did, never the earlier preview. */
export interface NoShowResultResponse {
  appointment:   AppointmentResponse;
  outcome:       NoShowOutcome;
  amount:        number;
  currency:      string;
  paymentStatus: string | null;
}

/**
 * GET /appointments/{id}/reschedule-preview — whether the appointment can be moved at all, decided by
 * the same server rule the save applies (status, then the client notice window). Never recomputed here.
 */
export interface ReschedulePreviewResponse {
  appointmentId:       string;
  canReschedule:       boolean;
  block:               "AppointmentClosed" | "InsideNoticeWindow" | null;
  /** The exact refusal the save would return; null when allowed. */
  message:             string | null;
  noticeWindowHours:   number;
  noticeWindowApplies: boolean;
}
