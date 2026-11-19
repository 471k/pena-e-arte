import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes, useLocation } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { platformApi } from "@/features/platform/platformApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { AuditLogPage } from "@/features/platform/components/AuditLogPage";
import type { AuditLogPageResponse } from "@/features/platform/platform.types";

const PAGE: AuditLogPageResponse = {
  items: [
    {
      id: "log-1", actorUserId: "u-1aaaaaaa-0000", actorRole: "admin",
      action: "Studio.Suspended", targetType: "Studio", targetId: "s-001",
      studioId: "s-001", metadata: '{"reasonCategory":"Fraud"}', createdAt: "2026-07-20T23:30:00Z",
      studioName: "Ink & Iron",
    },
    {
      id: "log-2", actorUserId: "u-2", actorRole: "admin",
      action: "Plan.Updated", targetType: "Plan", targetId: "p-001",
      studioId: null, metadata: "{}", createdAt: "2026-07-19T10:00:00Z", studioName: null,
    },
  ],
  totalCount: 2,
  page: 1,
  pageSize: 50,
};

const VOCAB = { actions: ["Plan.Updated", "Studio.Suspended"], targetTypes: ["Plan", "Platform", "Studio"] };
const STUDIOS = [{ id: "s-001", name: "Ink & Iron" }, { id: "s-002", name: "Black Rose" }];

const server = setupServer(
  http.get("http://localhost/api/v1/platform/audit-log/vocabulary", () => HttpResponse.json(VOCAB)),
  http.get("http://localhost/api/v1/platform/audit-log", () => HttpResponse.json(PAGE)),
  http.get("http://localhost/api/v1/studios", () => HttpResponse.json(STUDIOS)),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth:                      authReducer,
      [platformApi.reducerPath]: platformApi.reducer,
      [studiosApi.reducerPath]:  studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(platformApi.middleware, studiosApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u4", email: "admin@platform.test" }, token: "fake", tenantId: null, role: "admin" } as any,
    },
  });
}

function LocationProbe() {
  const location = useLocation();
  return <output data-testid="location">{location.search}</output>;
}

function renderPage(search = "") {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter initialEntries={[`/platform/audit-log${search}`]}>
        <Routes>
          <Route path="/platform/audit-log" element={<><AuditLogPage /><LocationProbe /></>} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

function captureRequests(): string[] {
  const urls: string[] = [];
  server.use(
    http.get("http://localhost/api/v1/platform/audit-log", ({ request }) => {
      urls.push(request.url);
      return HttpResponse.json(PAGE);
    }),
  );
  return urls;
}

describe("AuditLogPage", () => {
  it("renders the header", () => {
    renderPage();
    expect(screen.getByText("Audit Log")).toBeInTheDocument();
  });

  it("renders entries with action, target and the studio's name", async () => {
    renderPage();
    expect(await screen.findByRole("cell", { name: "Studio.Suspended" })).toBeInTheDocument();
    expect(screen.getByRole("cell", { name: "Plan.Updated" })).toBeInTheDocument();
    const firstRow = screen.getByRole("cell", { name: "Studio.Suspended" }).closest("tr")!;
    expect(within(firstRow).getByText("Ink & Iron")).toBeInTheDocument();
  });

  it("states that times are UTC and shows them on the UTC clock", async () => {
    renderPage();
    await screen.findByRole("cell", { name: "Studio.Suspended" });
    expect(screen.getByText(/times are in utc/i)).toBeInTheDocument();
    // 23:30Z must read 23:30 whatever zone the test runner is in.
    expect(screen.getByText(/20 Jul 2026.*23:30 UTC/)).toBeInTheDocument();
    expect(screen.getByLabelText(/to \(utc day, included\)/i)).toBeInTheDocument();
  });

  it("shows 'Platform-wide' for an entry with a null studioId", async () => {
    renderPage();
    await screen.findByRole("cell", { name: "Plan.Updated" });
    expect(screen.getByText("Platform-wide")).toBeInTheDocument();
  });

  it("shows a platform-wide backfill entry with target 'Platform' and no all-zero id", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/audit-log", () =>
        HttpResponse.json({
          items: [
            {
              id: "log-3", actorUserId: "u-1", actorRole: "admin",
              action: "Platform.RevenueLedgerBackfilled", targetType: "Platform",
              targetId: "00000000-0000-0000-0000-000000000000",
              studioId: null, metadata: '{"created":9}', createdAt: "2026-07-21T10:00:00Z",
            },
          ],
          totalCount: 1, page: 1, pageSize: 50,
        }),
      ),
    );
    renderPage();
    await screen.findByRole("cell", { name: "Platform.RevenueLedgerBackfilled" });
    expect(screen.getByRole("cell", { name: "Platform" })).toBeInTheDocument();
    expect(screen.queryByText("00000000")).not.toBeInTheDocument();
    expect(screen.getByText("Platform-wide")).toBeInTheDocument(); // the studio column
  });

  it("shows the total count in the header badge", async () => {
    renderPage();
    expect(await screen.findByText("2")).toBeInTheDocument();
  });

  it("shows an error message when the request fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/audit-log", () =>
        HttpResponse.json({ message: "fail" }, { status: 500 }),
      ),
    );
    renderPage();
    await waitFor(() => expect(screen.getByText(/failed to load the audit log/i)).toBeInTheDocument());
  });

  it("retries the request when 'Try again' is clicked", async () => {
    let requestCount = 0;
    server.use(
      http.get("http://localhost/api/v1/platform/audit-log", () => {
        requestCount += 1;
        return requestCount === 1
          ? HttpResponse.json({ message: "fail" }, { status: 500 })
          : HttpResponse.json(PAGE);
      }),
    );
    const user = userEvent.setup();
    renderPage();
    await user.click(await screen.findByRole("button", { name: /try again/i }));
    expect(await screen.findByRole("cell", { name: "Studio.Suspended" })).toBeInTheDocument();
  });

  it("shows an empty-state message when there are no entries", async () => {
    server.use(
      http.get("http://localhost/api/v1/platform/audit-log", () =>
        HttpResponse.json({ items: [], totalCount: 0, page: 1, pageSize: 50 }),
      ),
    );
    renderPage();
    expect(await screen.findByText(/no audit log entries match/i)).toBeInTheDocument();
  });

  it("picks the action from the named list, refetches with it and keeps it in the URL", async () => {
    const urls = captureRequests();
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("cell", { name: "Studio.Suspended" });
    await screen.findByRole("option", { name: "Plan.Updated" });

    await user.selectOptions(screen.getByLabelText(/^action$/i), "Plan.Updated");

    await waitFor(() => expect(urls.at(-1)).toContain("action=Plan.Updated"));
    expect(screen.getByTestId("location")).toHaveTextContent("action=Plan.Updated");
  });

  it("restores every filter and the page from a shared URL", async () => {
    const urls = captureRequests();
    renderPage("?action=Studio.Suspended&studioId=s-001&from=2026-07-01&to=2026-07-20&page=2");

    await waitFor(() => expect(urls.length).toBeGreaterThan(0));
    const sent = new URL(urls[0]).searchParams;
    expect(sent.get("action")).toBe("Studio.Suspended");
    expect(sent.get("studioId")).toBe("s-001");
    expect(sent.get("from")).toBe("2026-07-01");
    expect(sent.get("to")).toBe("2026-07-20");
    expect(sent.get("page")).toBe("2");
    expect(await screen.findByRole("option", { name: "Black Rose" })).toBeInTheDocument();
    expect(screen.getByLabelText(/^studio$/i)).toHaveValue("s-001");
  });

  it("pages to older entries and keeps the filters (more than 50 events)", async () => {
    const urls: string[] = [];
    server.use(
      http.get("http://localhost/api/v1/platform/audit-log", ({ request }) => {
        urls.push(request.url);
        return HttpResponse.json({ ...PAGE, totalCount: 120 });
      }),
    );
    const user = userEvent.setup();
    renderPage("?action=Studio.Suspended");

    expect(await screen.findByText("Page 1 of 3")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Newer" })).toBeDisabled();
    await user.click(screen.getByRole("button", { name: "Older" }));

    await waitFor(() => expect(new URL(urls.at(-1)!).searchParams.get("page")).toBe("2"));
    expect(new URL(urls.at(-1)!).searchParams.get("action")).toBe("Studio.Suspended");
    expect(screen.getByTestId("location")).toHaveTextContent("page=2");
  });

  it("goes back to page 1 when a filter changes", async () => {
    const user = userEvent.setup();
    renderPage("?page=3");
    await screen.findByRole("option", { name: "Plan.Updated" });

    await user.selectOptions(screen.getByLabelText(/^action$/i), "Plan.Updated");

    await waitFor(() => expect(screen.getByTestId("location")).not.toHaveTextContent("page="));
  });

  it("clears every filter at once", async () => {
    const user = userEvent.setup();
    renderPage("?action=Studio.Suspended&from=2026-07-01");
    await screen.findByRole("cell", { name: "Studio.Suspended" });

    await user.click(screen.getByRole("button", { name: /clear filters/i }));

    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent(/^$/));
  });

  it("opens a record's details: full ids, the recorded details, and a filter by its actor", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("cell", { name: "Studio.Suspended" });

    const toggle = screen.getByRole("button", { name: /show details for studio\.suspended/i });
    await user.click(toggle);

    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText("u-1aaaaaaa-0000")).toBeInTheDocument();
    expect(screen.getByText(/"reasonCategory": "Fraud"/)).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /only this actor's actions/i }));
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("actorUserId=u-1aaaaaaa-0000"));
    expect(screen.getByText(/showing one actor's actions/i)).toBeInTheDocument();
  });

  it("names staff actors by their current account name, never a client, and says when an account is gone", async () => {
    const base = PAGE.items[0];
    server.use(
      http.get("http://localhost/api/v1/platform/audit-log", () => HttpResponse.json({
        ...PAGE,
        items: [
          { ...base, id: "a1", action: "A.Named", actorRole: "owner", actorName: "Maria" },
          { ...base, id: "a2", action: "A.Deleted", actorRole: "artist", actorAccountDeleted: true },
          { ...base, id: "a3", action: "A.Client", actorRole: "client", actorName: "Should not show" },
          { ...base, id: "a4", action: "A.NoName", actorRole: "admin", actorName: null },
          { ...base, id: "a5", action: "A.Impersonating", actorRole: "admin-impersonating", actorName: "Sam" },
        ],
      })),
    );
    renderPage();
    await screen.findByRole("cell", { name: "A.Named" });

    const actorCell = (action: string) => within(screen.getByRole("cell", { name: action }).closest("tr")!).getAllByRole("cell").at(-1)!;
    expect(screen.getByRole("columnheader", { name: "Actor" })).toBeInTheDocument();
    expect(actorCell("A.Named")).toHaveTextContent(/^Maria · owner$/i);
    expect(actorCell("A.Deleted")).toHaveTextContent(/^Deleted user · artist$/i);
    expect(actorCell("A.Client")).toHaveTextContent(/^Client · client$/i);
    expect(screen.queryByText(/should not show/i)).not.toBeInTheDocument();
    expect(actorCell("A.NoName")).toHaveTextContent(/^No name on file · admin$/i);
    expect(actorCell("A.Impersonating")).toHaveTextContent(/^Sam · admin \(impersonating\)$/i);
  });

  it("says when an action recorded no details", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("cell", { name: "Plan.Updated" });
    await user.click(screen.getByRole("button", { name: /show details for plan\.updated/i }));
    expect(screen.getByText(/none recorded for this action/i)).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /only this studio/i })).not.toBeInTheDocument();
  });

  it("never puts anything but named choices, ids, dates and the page in the URL", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByRole("option", { name: "Black Rose" });
    await user.selectOptions(screen.getByLabelText(/^studio$/i), "s-002");
    await waitFor(() => expect(screen.getByTestId("location")).toHaveTextContent("studioId=s-002"));
    // The studio is carried by id; its name never reaches the URL.
    expect(screen.getByTestId("location")).not.toHaveTextContent(/Black|Rose/);
  });
});
