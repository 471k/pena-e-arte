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
