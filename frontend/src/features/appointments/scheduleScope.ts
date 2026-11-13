import { Role } from "@/shared/types/roles";

/**
 * Whose appointments the schedule is showing (E05c, O03). The scope always existed; it was just never
 * said on screen. Three cases:
 *
 * - "own": an artist (the server forces their own id whatever the URL says), or an owner in their own
 *   artist view, which OwnerLayout's artist-mode nav reaches with `?artistId=<their own artist id>`.
 * - "studio": an owner or admin with no filter, every artist's appointments plus requests with no
 *   artist yet.
 * - "artist": an owner or admin narrowed to one artist with the schedule's own filter.
 *
 * The owner's filter lives in its own URL parameter, `?artist=`, not in `?artistId=`. `?artistId=` set to
 * the owner's own artist id *is* artist mode (isInArtistContext), so an owner who picked themselves in a
 * filter that wrote `?artistId=` would have their whole menu switch to the artist one. Both parameters
 * drive the same server filter, GetAppointmentsQuery's `artistId`, which already narrows an owner's read to
 * one artist inside their own studio (the Appointment query filter is tenant-scoped).
 */
export type ScheduleScope =
  | { kind: "own" }
  | { kind: "studio" }
  | { kind: "artist"; artistId: string };

/** The owner's artist filter on /schedule. */
export const SCHEDULE_ARTIST_PARAM = "artist";
/** Artist mode's parameter (OwnerLayout's artist nav), kept for its existing meaning. */
export const ARTIST_MODE_PARAM = "artistId";

export interface ResolvedScheduleScope {
  scope: ScheduleScope;
  /** What GET /appointments receives as `artistId`, or undefined for none. */
  requestArtistId: string | undefined;
  /** An owner in their own artist view: raw-contact reminders and the Availability link use their id. */
  isOwnerInArtistMode: boolean;
}

export function resolveScheduleScope(role: Role | null | undefined, params: URLSearchParams): ResolvedScheduleScope {
  const modeArtistId = params.get(ARTIST_MODE_PARAM) || undefined;
  const filterArtistId = params.get(SCHEDULE_ARTIST_PARAM) || undefined;

  if (role === Role.Artist) {
    // Unchanged for an artist: whatever was in the URL is passed on and ignored by the server.
    return { scope: { kind: "own" }, requestArtistId: modeArtistId, isOwnerInArtistMode: false };
  }

  if (role === Role.Owner && modeArtistId) {
    return { scope: { kind: "own" }, requestArtistId: modeArtistId, isOwnerInArtistMode: true };
  }

  // An admin has no artist mode, so an `?artistId=` reaching an admin has always been a plain filter.
  const narrowedTo = filterArtistId ?? (role === Role.Admin ? modeArtistId : undefined);
  if (narrowedTo) {
    return { scope: { kind: "artist", artistId: narrowedTo }, requestArtistId: narrowedTo, isOwnerInArtistMode: false };
  }
  return { scope: { kind: "studio" }, requestArtistId: undefined, isOwnerInArtistMode: false };
}

/** The URL parameters for a new owner filter choice: `null` is the whole studio. Other parameters are kept. */
export function withArtistFilter(params: URLSearchParams, artistId: string | null): URLSearchParams {
  const next = new URLSearchParams(params);
  next.delete(ARTIST_MODE_PARAM);
  if (artistId) next.set(SCHEDULE_ARTIST_PARAM, artistId);
  else next.delete(SCHEDULE_ARTIST_PARAM);
  return next;
}
