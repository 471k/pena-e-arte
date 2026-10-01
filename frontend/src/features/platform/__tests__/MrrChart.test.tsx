import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { MrrChart } from "@/features/platform/components/MrrChart";
import type { MrrDataPoint } from "@/features/platform/platform.types";

const server = setupServer();

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function serveHistory(points: MrrDataPoint[]): void {
  server.use(http.get("http://localhost/api/v1/platform/mrr-history", () => HttpResponse.json(points)));
}

function renderChart(): void {
  const store = configureStore({
    reducer: {
      auth:                      authReducer,
      [platformApi.reducerPath]: platformApi.reducer,
    },
    middleware: (gd) => gd().concat(platformApi.middleware),
  });
  render(
    <Provider store={store}>
      <MrrChart />
    </Provider>,
  );
}

describe("MrrChart y-axis", () => {
  it("shows only a €0 baseline when every month is zero", async () => {
    serveHistory([
      { month: "2026-08", mrr: 0, isEstimated: true },
      { month: "2026-09", mrr: 0, isEstimated: true },
      { month: "2026-10", mrr: 0, isEstimated: true },
    ]);
    renderChart();

    expect(await screen.findByText("€0")).toBeInTheDocument();
    // The scale is floored at 1, which used to produce two identical "€1" labels.
    expect(screen.queryByText("€1")).not.toBeInTheDocument();
  });

  it("shows baseline, midpoint and top labels when there is revenue", async () => {
    serveHistory([
      { month: "2026-08", mrr: 100, isEstimated: false },
      { month: "2026-09", mrr: 200, isEstimated: false },
      { month: "2026-10", mrr: 400, isEstimated: false },
    ]);
    renderChart();

    expect(await screen.findByText("€0")).toBeInTheDocument();
    expect(screen.getByText("€200")).toBeInTheDocument();
    expect(screen.getByText("€400")).toBeInTheDocument();
  });
});
