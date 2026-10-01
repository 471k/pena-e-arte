import type { AppDispatch } from "@/app/store";
import { notificationsApi } from "./notificationsApi";
import { incrementUnread } from "./notificationsSlice";
import type { NotificationLogResponse } from "./notification.types";

// SentAt is stored with sub-second precision and serialised by the server; a small window around the
// pushed row's own timestamp is enough to find it without pulling the user's whole notification list.
const WINDOW_MS = 2000;

/**
 * "NotificationReceived" is broadcast to the whole studio group, but GET /notifications only returns
 * the rows the signed-in user may see (an artist sees only rows addressed to them). Counting every
 * push put a red badge on the bell with an empty list behind it. This asks the server the same
 * question the dropdown does, "is this exact row in my list?", with a tiny time-windowed request, and
 * only then counts it.
 *
 * Falls back to counting when the payload carries no id/time or the check fails: a missed
 * notification is worse than a stray badge.
 */
export async function incrementUnreadIfVisible(
  dispatch: AppDispatch,
  pushed: NotificationLogResponse | undefined,
): Promise<void> {
  const sentAtMs: number = pushed?.sentAt ? Date.parse(pushed.sentAt) : Number.NaN;
  if (!pushed?.id || Number.isNaN(sentAtMs)) {
    dispatch(incrementUnread());
    return;
  }

  try {
    const rows: NotificationLogResponse[] = await dispatch(
      notificationsApi.endpoints.getNotifications.initiate(
        {
          from: new Date(sentAtMs - WINDOW_MS).toISOString(),
          to:   new Date(sentAtMs + WINDOW_MS).toISOString(),
        },
        { forceRefetch: true, subscribe: false },
      ),
    ).unwrap();

    if (rows.some((row) => row.id === pushed.id)) dispatch(incrementUnread());
  } catch {
    dispatch(incrementUnread());
  }
}
