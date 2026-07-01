import { describe, it, expect } from "vitest";
import { configureStore, combineReducers } from "@reduxjs/toolkit";
import type { UnknownAction } from "@reduxjs/toolkit";
import authReducer, { logout } from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import notificationsReducer from "@/features/notifications/notificationsSlice";
import { artistsApi } from "@/features/artists/artistsApi";
import { studiosApi } from "@/features/studios/studiosApi";
import type { ArtistResponse } from "@/features/artists/artistsApi";

// Mirror the rootReducer pattern from store.ts (subset of slices is enough to
// verify the behaviour without pulling in every API in the app).
function makeStore() {
  const appReducer = combineReducers({
    auth:          authReducer,
    ui:            uiReducer,
    notifications: notificationsReducer,
    [artistsApi.reducerPath]: artistsApi.reducer,
    [studiosApi.reducerPath]: studiosApi.reducer,
  });
  type S = ReturnType<typeof appReducer>;
  const rootReducer = (state: S | undefined, action: UnknownAction): S =>
    appReducer(action.type === logout.type ? undefined : state, action);
  return configureStore({
    reducer:    rootReducer,
    middleware: (get) => get().concat(artistsApi.middleware, studiosApi.middleware),
  });
}

const MARCO: ArtistResponse = {
  id:              "marco-id",
  studioId:        "ink-soul-id",
  userId:          "user-marco",
  firstName:       "Marco",
  lastName:        "Santos",
  email:           "marco.santos@ink-soul.test",
  specializations: null,
  hourlyRate:      null,
  isActive:        true,
  avatarUrl:       null,
  portfolioImages: ["https://r2.example.com/tattoo1.jpg"],
  slug:            "marco-santos",
  createdAt:       "2026-01-01T00:00:00Z",
  updatedAt:       "2026-01-01T00:00:00Z",
};

describe("store — logout wipes API caches", () => {
  it("prevents stale cross-tenant data leaking to the next user session", async () => {
    const store = makeStore();

    // Simulate Marco's session: seed the artists API cache (mirrors what
    // useGetMyArtistQuery populates after a successful /artists/me call).
    await store.dispatch(artistsApi.util.upsertQueryData("getMyArtist", undefined, MARCO));

    const before = store.getState() as ReturnType<typeof store.getState> & { artistsApi: { queries: Record<string, unknown> } };
    expect(Object.keys(before.artistsApi.queries).length).toBeGreaterThan(0);

    // Marco logs out.
    store.dispatch(logout());

    const after = store.getState() as ReturnType<typeof store.getState> & {
      artistsApi: { queries: Record<string, unknown> };
      studiosApi: { queries: Record<string, unknown> };
    };

    // All API query caches must be empty — a freshly logged-in user (Luis)
    // cannot be served Marco's cached ArtistResponse by useGetMyArtistQuery.
    expect(Object.keys(after.artistsApi.queries)).toHaveLength(0);
    expect(Object.keys(after.studiosApi.queries)).toHaveLength(0);
    expect(after.auth.token).toBeNull();
  });
});
