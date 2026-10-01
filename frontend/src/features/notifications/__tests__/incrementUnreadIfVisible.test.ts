import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import type { AppDispatch } from "@/app/store";
import authReducer from "@/features/auth/authSlice";
import notificationsReducer from "../notificationsSlice";
import { notificationsApi } from "../notificationsApi";
import { incrementUnreadIfVisible } from "../incrementUnreadIfVisible";
import type { NotificationLogResponse } from "../notification.types";

const SENT_AT = "2026-10-01T20:43:10.000Z";

function pushedRow(overrides: Partial<NotificationLogResponse> = {}): NotificationLogResponse {
  return {
    id: "log-1", recipientId: null, recipientName: "Test", channel: "Sms",
    subject: null, body: "Hi Test", sentAt: SENT_AT, isSuccess: true, createdAt: SENT_AT,
    ...overrides,
  };
}

let requestedUrls: string[] = [];
const server = setupServer();

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); requestedUrls = []; });
afterAll(() => server.close());

function serveList(rows: NotificationLogResponse[] | "error"): void {
  server.use(http.get("http://localhost/api/v1/notifications", ({ request }) => {
    requestedUrls.push(request.url);
    return rows === "error" ? new HttpResponse(null, { status: 500 }) : HttpResponse.json(rows);
  }));
}

function makeStore() {
  const store = configureStore({
    reducer: {
      auth:                         authReducer,
      notifications:                notificationsReducer,
      [notificationsApi.reducerPath]: notificationsApi.reducer,
    },
    middleware: (gd) => gd().concat(notificationsApi.middleware),
  });
  return { store, dispatch: store.dispatch as unknown as AppDispatch };
}

describe("incrementUnreadIfVisible", () => {
  it("counts the push when the row is in the user's own notification list", async () => {
    serveList([pushedRow()]);
    const { store, dispatch } = makeStore();

    await incrementUnreadIfVisible(dispatch, pushedRow());

    expect(store.getState().notifications.unreadCount).toBe(1);
  });

  it("does not count a push the server does not return to this user (an artist, a row addressed to a client)", async () => {
    serveList([]);
    const { store, dispatch } = makeStore();

    await incrementUnreadIfVisible(dispatch, pushedRow());

    expect(store.getState().notifications.unreadCount).toBe(0);
  });

  it("asks only for a narrow window around the pushed row, never the whole list", async () => {
    serveList([pushedRow()]);
    const { dispatch } = makeStore();

    await incrementUnreadIfVisible(dispatch, pushedRow());

    expect(requestedUrls).toHaveLength(1);
    const url: URL = new URL(requestedUrls[0]);
    expect(url.searchParams.get("from")).toBe("2026-10-01T20:43:08.000Z");
    expect(url.searchParams.get("to")).toBe("2026-10-01T20:43:12.000Z");
  });

  it("still counts when the visibility check fails: a missed notification is worse than a stray badge", async () => {
    serveList("error");
    const { store, dispatch } = makeStore();

    await incrementUnreadIfVisible(dispatch, pushedRow());

    expect(store.getState().notifications.unreadCount).toBe(1);
  });

  it("counts without a request when the push carries no payload", async () => {
    const { store, dispatch } = makeStore();

    await incrementUnreadIfVisible(dispatch, undefined);

    expect(store.getState().notifications.unreadCount).toBe(1);
    expect(requestedUrls).toHaveLength(0);
  });

  it("counts without a request when the payload has no sent time", async () => {
    const { store, dispatch } = makeStore();

    await incrementUnreadIfVisible(dispatch, pushedRow({ sentAt: null }));

    expect(store.getState().notifications.unreadCount).toBe(1);
    expect(requestedUrls).toHaveLength(0);
  });
});
