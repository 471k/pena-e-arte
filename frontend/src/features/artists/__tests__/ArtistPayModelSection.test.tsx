import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { artistsApi, type ArtistPayModel, type UpdateArtistPayModelRequest } from "@/features/artists/artistsApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { ArtistPayModelSection } from "@/features/artists/components/ArtistPayModelSection";

// A07a phase 4: one artist's own pay terms, owner only. Empty fields use the studio's.

let artistTerms: ArtistPayModel = { artistId: "art-1", arrangement: null, commissionRate: null, flatFeePerSession: null };
let artistStatus = 200;
let saved: UpdateArtistPayModelRequest | null = null;

const server = setupServer(
  http.get("http://localhost/api/v1/artists/art-1/pay-model", () =>
    artistStatus === 200 ? HttpResponse.json(artistTerms) : HttpResponse.json({ title: "boom" }, { status: artistStatus })),
  http.put("http://localhost/api/v1/artists/art-1/pay-model", async ({ request }) => {
    saved = (await request.json()) as UpdateArtistPayModelRequest;
    artistTerms = { artistId: "art-1", ...saved };
    return HttpResponse.json(artistTerms);
  }),
  http.get("http://localhost/api/v1/studios/me/pay-model", () =>
    HttpResponse.json({ arrangement: "Commission", commissionRate: 50, flatFeePerSession: null, reviewedAt: "2026-11-01T10:00:00Z", currency: "ALL" })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => {
  server.resetHandlers(); cleanup(); saved = null; artistStatus = 200;
  artistTerms = { artistId: "art-1", arrangement: null, commissionRate: null, flatFeePerSession: null };
});
afterAll(() => server.close());

function renderSection() {
  const store = configureStore({
    reducer: {
      auth: authReducer, ui: uiReducer,
      [artistsApi.reducerPath]: artistsApi.reducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(artistsApi.middleware, studiosApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "owner@ink.test" }, token: "t", tenantId: "stud-0001", role: "owner", pendingReferralCode: null, impersonation: null } as any,
    },
  });
  render(<Provider store={store}><ArtistPayModelSection artistId="art-1" /></Provider>);
}

async function openSection() {
  const user = userEvent.setup();
  renderSection();
  await user.click(screen.getByRole("button", { name: /How this artist gets paid/ }));
  return user;
}

describe("ArtistPayModelSection", () => {
  it("stays collapsed, and reads nothing, until opened", () => {
    renderSection();
    expect(screen.getByRole("button", { name: /How this artist gets paid/ })).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByTestId("artist-pay-model-loading")).not.toBeInTheDocument();
  });

  it("shows a loading state, then the studio's terms as the fallback", async () => {
    await openSection();
    const select = await screen.findByLabelText("Arrangement");
    expect(select).toHaveValue("");
    expect(screen.getByRole("option", { name: "Studio's terms (Commission)" })).toBeInTheDocument();
    expect(screen.getByLabelText(/This artist's share/)).toHaveValue("");
    expect(screen.getByText("Empty uses the studio's rate, 50%.")).toBeInTheDocument();
  });

  it("says it couldn't load, with a retry, when the read fails", async () => {
    artistStatus = 500;
    await openSection();
    expect(await screen.findByRole("alert")).toHaveTextContent("We couldn't load this artist's pay terms.");
  });

  it("saves this artist's own rate, leaving the arrangement as the studio's", async () => {
    const user = await openSection();
    await user.type(await screen.findByLabelText(/This artist's share/), "70");
    await user.click(screen.getByRole("button", { name: "Save pay terms" }));
    await screen.findByText("Overrides the studio's rate for this artist.");
    expect(saved).toEqual({ arrangement: null, commissionRate: 70, flatFeePerSession: null });
  });

  it("asks for a fee when this artist is on a flat fee", async () => {
    const user = await openSection();
    await user.selectOptions(await screen.findByLabelText("Arrangement"), "FlatFeePerSession");
    expect(screen.getByLabelText(/fee per session for this artist/)).toBeInTheDocument();
    expect(screen.queryByLabelText(/This artist's share/)).not.toBeInTheDocument();
  });
});
