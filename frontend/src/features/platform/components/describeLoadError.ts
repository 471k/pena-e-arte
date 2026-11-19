import type { FetchBaseQueryError } from "@reduxjs/toolkit/query";
import type { SerializedError } from "@reduxjs/toolkit";

export interface LoadFailure {
  message:   string;
  /** False when retrying cannot help (not found, no access); true for a server error or a lost connection. */
  retryable: boolean;
}

/**
 * D02: name why a read failed, because each cause has its own way forward. A 404 is a fact about the record;
 * a 500 or a dropped connection is a fact about this attempt, and only those are worth a Try again.
 */
export function describeLoadError(error: FetchBaseQueryError | SerializedError | undefined, noun: string): LoadFailure {
  const status = error && "status" in error ? error.status : undefined;
  const Noun = noun.charAt(0).toUpperCase() + noun.slice(1);
  if (status === 404) return { message: `${Noun} not found. It doesn't exist, or it was removed.`, retryable: false };
  if (status === 403 || status === 401) return { message: `You don't have access to this ${noun}.`, retryable: false };
  if (status === "FETCH_ERROR" || status === "TIMEOUT_ERROR") {
    return { message: `Couldn't reach the server to load this ${noun}. Check your connection.`, retryable: true };
  }
  return { message: `Couldn't load this ${noun}. This is a problem on our side, not with the ${noun}.`, retryable: true };
}
