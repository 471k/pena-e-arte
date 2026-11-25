import type { TattooIntakeValues } from "./components/tattooIntakeValidation";

/**
 * Saved booking drafts (S04): what a signed-in client's booking form keeps between visits, at one studio, for seven
 * days from the last change. Phi's decision: everything EXCEPT the safety notes ("Anything else I should know?",
 * which invites medical details). That field is never written to the draft in any form, not even as an empty
 * string, so the payload is built field by field and never by spreading the intake values. The server refuses a
 * payload that still carries the key (SaveMyBookingDraftHandler).
 */

export interface BookingDraftImage {
  url:      string;
  category: "AreaPhoto" | "Reference";
}

export interface BookingDraftResponse {
  payload:   string;
  images:    BookingDraftImage[];
  savedAt:   string;
  expiresAt: string;
}

/**
 * C05: what following a "booking attempt" email's resume link did. "Expired" carries no draft; every other failure
 * is one 404 from the server, deliberately the same for a forged link, a draft that is gone, and someone else's.
 */
export interface ResumeBookingDraftResponse {
  outcome: "Resumed" | "AlreadyClaimed" | "Expired";
  draft:   BookingDraftResponse | null;
}

/** The resume token from the link's URL fragment (`#t=…`), which browsers never send to a server. */
export function readResumeTokenFromHash(hash: string): string | null {
  const params = new URLSearchParams(hash.startsWith("#") ? hash.slice(1) : hash);
  const token = params.get("t");
  return token && token.length > 0 ? token : null;
}

export interface SaveBookingDraftRequest {
  payload: string;
  images:  BookingDraftImage[];
}

/** The form fields a draft keeps. clientId is left out: a client always books as themselves. */
export interface DraftFormValues {
  artistId:        string | null;
  bookAnyArtist:   boolean;
  scheduledAt:     string;
  durationMinutes: number;
  serviceId:       string | null;
  promoCode:       string;
  notes:           string;
}

/** The intake answers a draft keeps: all of them but the safety notes. */
export type DraftIntakeValues = Omit<TattooIntakeValues, "safetyNotes">;

export interface BookingDraftContent {
  form:             DraftFormValues;
  intake:           DraftIntakeValues;
  desiredPlacement: string[];
  referralCode:     string;
}

/** The draft's JSON. Each kept field is named, so a new intake field is left out until someone decides to keep it. */
export function draftPayload(
  form: DraftFormValues,
  intake: TattooIntakeValues,
  desiredPlacement: string[],
  referralCode: string,
): string {
  const content: BookingDraftContent = {
    form,
    intake: {
      tattooDescription:   intake.tattooDescription,
      style:               intake.style,
      referralSource:      intake.referralSource,
      referralSourceOther: intake.referralSourceOther,
    },
    desiredPlacement,
    referralCode,
  };
  return JSON.stringify(content);
}

const str = (v: unknown): string => (typeof v === "string" ? v : "");

/** Reads a saved draft back, tolerating anything missing or malformed: a bad field becomes its empty value. */
export function readDraft(payload: string): BookingDraftContent | null {
  let raw: unknown;
  try {
    raw = JSON.parse(payload);
  } catch {
    return null; // not a draft this form wrote: offer nothing rather than restore garbage
  }
  if (!raw || typeof raw !== "object") return null;
  const r = raw as Record<string, unknown>;
  const f = (r.form && typeof r.form === "object" ? r.form : {}) as Record<string, unknown>;
  const i = (r.intake && typeof r.intake === "object" ? r.intake : {}) as Record<string, unknown>;
  return {
    form: {
      artistId:        typeof f.artistId === "string" ? f.artistId : null,
      bookAnyArtist:   f.bookAnyArtist === true,
      scheduledAt:     str(f.scheduledAt),
      durationMinutes: typeof f.durationMinutes === "number" && f.durationMinutes > 0 ? f.durationMinutes : 60,
      serviceId:       typeof f.serviceId === "string" ? f.serviceId : null,
      promoCode:       str(f.promoCode),
      notes:           str(f.notes),
    },
    intake: {
      tattooDescription:   str(i.tattooDescription),
      style:               str(i.style),
      referralSource:      str(i.referralSource),
      referralSourceOther: str(i.referralSourceOther),
    },
    desiredPlacement: Array.isArray(r.desiredPlacement) ? r.desiredPlacement.filter((p): p is string => typeof p === "string") : [],
    referralCode:     str(r.referralCode),
  };
}
