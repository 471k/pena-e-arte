import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import { Provider } from "react-redux";
import { MemoryRouter } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { HelpInsightsPage } from "@/features/platform/components/HelpInsightsPage";
import type { HelpSearchInsightsResponse } from "@/features/platform/platform.types";

const INSIGHTS: HelpSearchInsightsResponse = {
  totalSearches: 12,
  days: 30,
  topQueries: [
    { query: "book appointment", count: 5, rolesAsked: ["client", "artist"] },
    { query: "deposit rules", count: 3, rolesAsked: ["owner"] },
  ],
  zeroResultQueries: [
    { query: "obscure feature", count: 2, rolesAsked: ["owner"] },
  ],
};

const server = setupServer(
  http.get("http://localhost/api/v1/platform/help-search-insights", () =>
    HttpResponse.json(INSIGHTS),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      [platformApi.reducerPath]: platformApi.reducer,
    },
    middleware: (gd) => gd().concat(platformApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u4", email: "issuer@platform.test" }, token: "fake", tenantId: null, role: "issuer" } as any,
    },
  });
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <HelpInsightsPage />
      </MemoryRouter>
    </Provider>,
  );
}

describe("HelpInsightsPage", () => {
  it("renders the header", () => {
    renderPage();
    expect(screen.getByText(/help search insights/i)).toBeInTheDocument();
  });

  it("renders both the top-queries and zero-result tables", async () => {
    renderPage();

    expect(await screen.findByText("book appointment")).toBeInTheDocument();
    expect(screen.getByText("deposit rules")).toBeInTheDocument();
    expect(screen.getByText("obscure feature")).toBeInTheDocument();
  });

  it("shows the total search count in the header badge", async () => {
    renderPage();
    expect(await screen.findByText(/12 searches/i)).toBeInTheDocument();
  });

  it("renders role chips for each query", async () => {
    renderPage();
    await screen.findByText("book appointment");
    expect(screen.getAllByText("client").length).toBeGreaterThan(0);
    expect(screen.getAllByText("artist").length).toBeGreaterThan(0);
  });

  it("shows an empty-state message when there are no zero-result queries", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/help-search-insights", () =>
        HttpResponse.json({ ...INSIGHTS, zeroResultQueries: [] }),
      ),
    );
    renderPage();

    expect(await screen.findByText(/no zero-result searches/i)).toBeInTheDocument();
  });

  it("shows an error message when the request fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/help-search-insights", () =>
        HttpResponse.json({ message: "fail" }, { status: 500 }),
      ),
    );
    renderPage();

    await waitFor(() => expect(screen.getByText(/failed to load help search insights/i)).toBeInTheDocument());
  });
});
