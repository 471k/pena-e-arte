import { withStudioTimeZone } from "@/shared/utils/formatInStudioTimezone";
import type { DesignResponse, DesignWaitingOn } from "./design.types";

// E04c (A05): whose move it is on a design, worded for whoever is looking. The state itself is derived
// on the server (DesignReviewState: only the latest revision decides); this file only words it.

export type DesignViewer = "artist" | "owner" | "client";

/** Short label for a design list row, or null when nothing is outstanding. */
export function waitingLabel(waitingOn: DesignWaitingOn, viewer: DesignViewer): string | null {
  if (waitingOn === "Nobody") return null;
  if (viewer === "client") return waitingOn === "Client" ? "Waiting on you" : "Waiting on your artist";
  if (viewer === "artist") return waitingOn === "Artist" ? "Waiting on you" : "Waiting on client";
  return waitingOn === "Artist" ? "Waiting on artist" : "Waiting on client";
}

/** "5 Nov 2026, 10:30" in the studio's timezone. */
export function formatStudioDateTime(iso: string, timeZone: string | undefined): string {
  return new Date(iso).toLocaleString("en-GB", withStudioTimeZone({
    day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit", hourCycle: "h23",
  }, timeZone));
}

/**
 * The detail page's one-line state, naming the version it is about. `latestApproval` is the latest
 * revision's own approval status (it tells an expired review window apart from requested changes).
 */
export function waitingMessage(
  design: Pick<DesignResponse, "waitingOn" | "status" | "latestVersionNumber" | "latestUploadedAt">,
  viewer: DesignViewer,
  latestApproval: string | null | undefined,
  timeZone: string | undefined,
): string {
  const v = design.latestVersionNumber;
  const version = v != null ? `version ${v}` : null;
  const uploaded = design.latestUploadedAt ? `, uploaded ${formatStudioDateTime(design.latestUploadedAt, timeZone)}` : "";
  const expired = latestApproval === "Expired";

  if (design.waitingOn === "Nobody") {
    return version && design.status === "Approved" ? `${cap(version)} is approved.` : "Nothing is waiting on anyone.";
  }

  if (viewer === "client") {
    if (design.waitingOn === "Client") return `Your artist is waiting for your feedback on ${version ?? "the latest version"}${uploaded}.`;
    if (!version) return "Waiting on your artist: they haven't uploaded a version yet.";
    if (expired) return `Waiting on your artist: the review window for ${version} closed without an answer.`;
    return `Waiting on your artist: you asked for changes to ${version}, and they're working on the next one.`;
  }

  if (design.waitingOn === "Client") {
    return `Waiting on the client: ${version ?? "the latest version"}${uploaded} is with them for review.`;
  }
  if (!version) return "Waiting on the artist: no version has been uploaded yet.";
  if (expired) return `The client didn't review ${version} in time. Upload a new revision, or ask them to review it.`;
  return `The client has requested changes to ${version}. Upload a new revision to continue.`;
}

function cap(s: string): string {
  return s.charAt(0).toUpperCase() + s.slice(1);
}
