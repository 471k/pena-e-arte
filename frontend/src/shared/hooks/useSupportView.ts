import { useAppSelector } from "@/app/hooks";

/** The sentence every read-only control and page uses, so a support session reads the same everywhere. */
export const SUPPORT_VIEW_READ_ONLY_REASON =
  "This is a read-only support session: changes are turned off. End the session to make changes as yourself.";

/**
 * Where a support session starts and where read-only pages send the admin back to. Not the dashboard: none of
 * its reads are on the server's support allow-list, so it could only show errors; the schedule's appointment
 * read is (ImpersonationAllowList: GET /api/v1/appointments).
 */
export const SUPPORT_VIEW_HOME = "/schedule";

/**
 * Whether the signed-in admin is viewing a studio in a Support Impersonation session (D05). The server allows
 * GET only in that session (ImpersonationAllowList); this lets the owner shell say so instead of offering
 * controls that would fail.
 */
export function useSupportView(): boolean {
  // Loose on purpose: a state with no impersonation value at all is not a session (only one that names one is).
  return useAppSelector((s) => s.auth.impersonation != null);
}
