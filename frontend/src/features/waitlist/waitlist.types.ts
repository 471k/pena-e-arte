export const WaitlistStatus = {
  Waiting:   "Waiting",
  Notified:  "Notified",
  Booked:    "Booked",
  Expired:   "Expired",
  Cancelled: "Cancelled",
} as const;
export type WaitlistStatus = (typeof WaitlistStatus)[keyof typeof WaitlistStatus];

export interface WaitlistEntryResponse {
  id:                string;
  studioId:          string;
  artistId:          string | null;
  artistName:        string | null;
  clientId:          string | null;
  clientName:        string | null;
  guestName:         string | null;
  guestEmail:        string | null;
  guestPhone:        string | null;
  preferredDateFrom: string;
  preferredDateTo:   string;
  status:            WaitlistStatus;
  notifiedAt:        string | null;
  notes:             string | null;
  createdAt:         string;
  /** C09: the start of the slot this entry was offered; null while waiting. */
  notifiedSlotStart: string | null;
  /** C09: only on the client's own list, for an offer still open: opens the claim page. */
  claimToken:        string | null;
}

/** What the claim page can say about an offer (WaitlistClaimStates on the server). */
export const WaitlistClaimState = {
  Claimable:   "Claimable",
  SlotTaken:   "SlotTaken",
  Booked:      "Booked",
  WindowEnded: "WindowEnded",
  Cancelled:   "Cancelled",
} as const;
export type WaitlistClaimState = (typeof WaitlistClaimState)[keyof typeof WaitlistClaimState];

/** The claim page's read: no personal data, only the offer. */
export interface WaitlistClaimResponse {
  state:           WaitlistClaimState;
  studioSlug:      string;
  studioName:      string;
  studioTimezone:  string;
  artistId:        string | null;
  artistName:      string | null;
  slotStart:       string;
  durationMinutes: number;
  windowEndsAt:    string;
}

export interface JoinWaitlistRequest {
  studioSlug:        string | null;
  artistId:          string | null;
  preferredDateFrom: string;
  preferredDateTo:   string;
  guestName:         string | null;
  guestEmail:        string | null;
  guestPhone:        string | null;
  notes:             string | null;
}
