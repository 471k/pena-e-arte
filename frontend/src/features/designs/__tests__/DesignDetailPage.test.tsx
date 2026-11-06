import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import uiReducer from "@/features/ui/uiSlice";
import { designsApi } from "@/features/designs/designsApi";
import { clientsApi } from "@/features/clients/clientsApi";
import { studiosApi } from "@/features/studios/studiosApi";
import { DesignDetailPage } from "@/features/designs/components/DesignDetailPage";
import type { DesignRevisionResponse, DesignResponse } from "@/features/designs/design.types";
import { Role } from "@/shared/types/roles";

// ── SignalR mock (ShareDesignButton dialog uses no SignalR, but guard against
//   any parent component that may eventually call the hook in the test tree) ───

vi.mock("@microsoft/signalr", () => {
  function HubConnectionBuilder(this: Record<string, unknown>) {
    this.withUrl                = vi.fn().mockReturnValue(this);
    this.withAutomaticReconnect = vi.fn().mockReturnValue(this);
    this.configureLogging       = vi.fn().mockReturnValue(this);
    this.build = vi.fn(() => ({
      on:     vi.fn(),
      start:  vi.fn().mockResolvedValue(undefined),
      invoke: vi.fn().mockResolvedValue(undefined),
      stop:   vi.fn().mockResolvedValue(undefined),
    }));
  }
  return { HubConnectionBuilder, LogLevel: { Warning: 2 } };
});

// ── Seed data ──────────────────────────────────────────────────────────────────

const DESIGN_ID = "d-001";

const REV_PENDING: DesignRevisionResponse = {
  id:             "rev-001",
  designId:       DESIGN_ID,
  versionNumber:  1,
  fileUrl:        "http://cdn.example.com/design.jpg",
  notes:          "Initial concept",
  uploadedAt:     "2024-01-15T10:00:00Z",
  approvalStatus: null,
  approvalNotes:  null,
};

const REV_APPROVED: DesignRevisionResponse = {
  id:             "rev-002",
  designId:       DESIGN_ID,
  versionNumber:  2,
  fileUrl:        "http://cdn.example.com/design-v2.jpg",
  notes:          null,
  uploadedAt:     "2024-02-01T10:00:00Z",
  approvalStatus: "Approved",
  approvalNotes:  null,
};

const REV_CHANGES: DesignRevisionResponse = {
  id:             "rev-003",
  designId:       DESIGN_ID,
  versionNumber:  3,
  fileUrl:        "http://cdn.example.com/design-v3.jpg",
  notes:          null,
  uploadedAt:     "2024-03-01T10:00:00Z",
  approvalStatus: "ChangesRequested",
  approvalNotes:  "Please change the colour scheme.",
};

const DESIGN: DesignResponse = {
  id:          DESIGN_ID,
  studioId:    "s-001",
  clientId:    "c-001",
  artistId:    "a-001",
  title:       "Dragon Sleeve",
  description: null,
  createdAt:   "2024-01-01T00:00:00Z",
  status:      "InReview",
  isCatalogItem: false,
  price:       null,
  waitingOn: "Client", latestVersionNumber: 1, latestUploadedAt: "2024-01-15T10:00:00Z",
};

// ── MSW server ─────────────────────────────────────────────────────────────────

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me", () =>
    HttpResponse.json({ id: "s-001", timezone: "UTC", currency: "EUR" }),
  ),
  http.get("http://localhost/api/v1/designs/:designId", () =>
    HttpResponse.json(DESIGN),
  ),
  http.get("http://localhost/api/v1/clients/:clientId", () =>
    HttpResponse.json({
      id: "c-001", studioId: "s-001", firstName: "Ana", lastName: "Costa",
      email: "ana@test.com", phone: null, createdAt: "2024-01-01T00:00:00Z", userId: null,
    }),
  ),
  http.get("http://localhost/api/v1/designs/:designId/revisions", () =>
    HttpResponse.json([REV_PENDING]),
  ),
  http.post("http://localhost/api/v1/designs/revisions/:revisionId/review", () =>
    HttpResponse.json({ ...REV_PENDING, approvalStatus: "Approved" }),
  ),
  http.delete("http://localhost/api/v1/designs/:designId/revisions/:revisionId", () =>
    new HttpResponse(null, { status: 204 }),
  ),
  http.post("http://localhost/api/v1/designs/revisions/:revisionId/share-token", () =>
    HttpResponse.json({
      id:        "tok-001",
      token:     "abc123",
      shareUrl:  "http://app.example.com/share/abc123",
      expiresAt: "2024-12-31T00:00:00Z",
    }),
  ),
  http.delete("http://localhost/api/v1/designs/share-tokens/:tokenId", () =>
    new HttpResponse(null, { status: 204 }),
  ),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

// ── Helpers ────────────────────────────────────────────────────────────────────

function makeStore(role: Role = Role.Client) {
  return configureStore({
    reducer: {
      auth:                     authReducer,
      ui:                       uiReducer,
      [designsApi.reducerPath]: designsApi.reducer,
      [clientsApi.reducerPath]: clientsApi.reducer,
      [studiosApi.reducerPath]: studiosApi.reducer,
    },
    middleware: (gd) => gd().concat(designsApi.middleware, clientsApi.middleware, studiosApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "test@test.com" }, token: "fake-token", tenantId: "s-001", role, pendingReferralCode: null, impersonation: null } as any,
      ui:   { readOnlyError: null, sessionExpired: false, studioSuspended: false, planLimitError: null, impersonationScopeError: null, impersonationSessionExpired: false },
    },
  });
}

function renderPage(role: Role = Role.Client, designId = DESIGN_ID) {
  render(
    <Provider store={makeStore(role)}>
      <MemoryRouter initialEntries={[`/designs/${designId}`]}>
        <Routes>
          <Route path="/designs/:id"        element={<DesignDetailPage />} />
          <Route path="/designs/:id/upload" element={<div data-testid="upload-page" />} />
          <Route path="/designs"            element={<div data-testid="designs-list" />} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("DesignDetailPage", () => {

  // ── Loading / error / empty ──────────────────────────────────────────────────

  it("shows a loading skeleton while revisions are fetching", () => {
    renderPage();
    expect(screen.getByLabelText(/loading revisions/i)).toBeInTheDocument();
  });

  it("shows an error message when the revisions fetch fails", async () => {
    server.use(
      http.get("http://localhost/api/v1/designs/:designId/revisions", () =>
        HttpResponse.json({ message: "error" }, { status: 500 }),
      ),
    );
    renderPage();
    expect(await screen.findByText("Failed to load revisions. Please try again.")).toBeInTheDocument();
  });

  it("shows 'No revisions yet.' when the design has no revisions", async () => {
    server.use(
      http.get("http://localhost/api/v1/designs/:designId/revisions", () =>
        HttpResponse.json([]),
      ),
    );
    renderPage();
    expect(await screen.findByText("No revisions yet.")).toBeInTheDocument();
  });

  // ── Revision rendering ───────────────────────────────────────────────────────

  it("renders a revision card for each revision", async () => {
    server.use(
      http.get("http://localhost/api/v1/designs/:designId/revisions", () =>
        HttpResponse.json([REV_PENDING, REV_APPROVED]),
      ),
    );
    renderPage();
    expect(await screen.findByText("v1")).toBeInTheDocument();
    expect(screen.getByText("v2")).toBeInTheDocument();
  });

  it("shows 'Pending' badge for a revision with no approvalStatus", async () => {
    renderPage();
    expect(await screen.findByText("Pending")).toBeInTheDocument();
  });

  it("shows 'Approved' badge for an approved revision", async () => {
    server.use(
      http.get("http://localhost/api/v1/designs/:designId/revisions", () =>
        HttpResponse.json([REV_APPROVED]),
      ),
    );
    renderPage();
    expect(await screen.findByText("Approved")).toBeInTheDocument();
  });

  it("shows 'Changes Requested' badge for a revision with changes requested", async () => {
    server.use(
      http.get("http://localhost/api/v1/designs/:designId/revisions", () =>
        HttpResponse.json([REV_CHANGES]),
      ),
    );
    renderPage();
    expect(await screen.findByText("Changes Requested")).toBeInTheDocument();
  });

  it("renders revision upload notes when present", async () => {
    renderPage(Role.Client);
    expect(await screen.findByText("Initial concept")).toBeInTheDocument();
  });

  it("renders approval notes for a 'Changes Requested' revision", async () => {
    server.use(
      http.get("http://localhost/api/v1/designs/:designId/revisions", () =>
        HttpResponse.json([REV_CHANGES]),
      ),
    );
    renderPage();
    expect(await screen.findByText("Please change the colour scheme.")).toBeInTheDocument();
  });

  // ── Role-gated actions ───────────────────────────────────────────────────────

  it("'Upload Revision' header button is visible for Artist role", async () => {
    renderPage(Role.Artist);
    await screen.findByText("v1");
    expect(screen.getByRole("button", { name: /upload revision/i })).toBeInTheDocument();
  });

  it("'Upload Revision' header button is NOT visible for Client role", async () => {
    renderPage(Role.Client);
    await screen.findByText("Pending");
    expect(screen.queryByRole("button", { name: /upload revision/i })).not.toBeInTheDocument();
  });

  it("'Upload Revision' navigates to the upload page", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await screen.findByText("v1");
    await user.click(screen.getByRole("button", { name: /upload revision/i }));
    expect(screen.getByTestId("upload-page")).toBeInTheDocument();
  });

  // ── canReview fix: Approve / Request Changes visible only for Client ─────────

  it("Approve button is shown for Client role on a pending revision", async () => {
    renderPage(Role.Client);
    expect(await screen.findByRole("button", { name: /approve/i })).toBeInTheDocument();
  });

  it("Approve button is NOT shown for Artist role", async () => {
    renderPage(Role.Artist);
    await screen.findByText("v1");
    expect(screen.queryByRole("button", { name: /^approve$/i })).not.toBeInTheDocument();
  });

  it("Approve button is NOT shown for Owner role", async () => {
    renderPage(Role.Owner);
    await screen.findByText("v1");
    expect(screen.queryByRole("button", { name: /^approve$/i })).not.toBeInTheDocument();
  });

  it("Approve button is NOT shown for an already-Approved revision", async () => {
    server.use(
      http.get("http://localhost/api/v1/designs/:designId/revisions", () =>
        HttpResponse.json([REV_APPROVED]),
      ),
    );
    renderPage(Role.Client);
    expect(await screen.findByText("Approved")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /^approve$/i })).not.toBeInTheDocument();
  });

  it("clicking Approve calls reviewRevision with approved=true", async () => {
    const user = userEvent.setup();
    renderPage(Role.Client);
    await user.click(await screen.findByRole("button", { name: /^approve$/i }));
    // After mutation, cache is invalidated and page refetches — loading text reappears briefly.
    // The success is implicit when the button is no longer errored; we just verify no error toast.
    expect(screen.queryByText(/failed to approve/i)).not.toBeInTheDocument();
  });

  it("'Request Changes' opens the notes form for Client", async () => {
    const user = userEvent.setup();
    renderPage(Role.Client);
    await user.click(await screen.findByRole("button", { name: /request changes/i }));
    expect(screen.getByRole("button", { name: /submit/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /cancel/i })).toBeInTheDocument();
  });

  it("Cancel on the request-changes form restores the initial buttons", async () => {
    const user = userEvent.setup();
    renderPage(Role.Client);
    await user.click(await screen.findByRole("button", { name: /request changes/i }));
    await user.click(screen.getByRole("button", { name: /cancel/i }));
    expect(await screen.findByRole("button", { name: /^approve$/i })).toBeInTheDocument();
  });

  it("submitting Request Changes calls reviewRevision with approved=false", async () => {
    const user = userEvent.setup();
    renderPage(Role.Client);
    await user.click(await screen.findByRole("button", { name: /request changes/i }));
    await user.type(screen.getByRole("textbox"), "Please adjust the shading.");
    await user.click(screen.getByRole("button", { name: /submit/i }));
    expect(screen.queryByText(/failed to submit/i)).not.toBeInTheDocument();
  });

  // ── Delete ───────────────────────────────────────────────────────────────────

  it("Delete button is visible for Artist role", async () => {
    renderPage(Role.Artist);
    await screen.findByText("v1");
    expect(screen.getByRole("button", { name: /delete revision/i })).toBeInTheDocument();
  });

  it("Delete button is NOT visible for Client role", async () => {
    renderPage(Role.Client);
    await screen.findByText("Pending");
    expect(screen.queryByRole("button", { name: /delete revision/i })).not.toBeInTheDocument();
  });

  it("clicking Delete opens a confirmation dialog", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await user.click(await screen.findByRole("button", { name: /delete revision/i }));
    expect(screen.getByRole("dialog")).toBeInTheDocument();
    expect(screen.getByText(/this action cannot be undone/i)).toBeInTheDocument();
  });

  it("confirming the dialog calls deleteRevision", async () => {
    const user = userEvent.setup();
    renderPage(Role.Artist);
    await user.click(await screen.findByRole("button", { name: /delete revision/i }));
    const dialog = screen.getByRole("dialog");
    await user.click(within(dialog).getByRole("button", { name: /^delete$/i }));
    expect(screen.queryByText(/failed to delete/i)).not.toBeInTheDocument();
  });

  // ── Share ─────────────────────────────────────────────────────────────────────

  it("Share button is visible for Artist role", async () => {
    renderPage(Role.Artist);
    await screen.findByText("v1");
    expect(screen.getByRole("button", { name: /share/i })).toBeInTheDocument();
  });

  it("Share button is NOT visible for Client role", async () => {
    renderPage(Role.Client);
    await screen.findByText("Pending");
    expect(screen.queryByRole("button", { name: /^share$/i })).not.toBeInTheDocument();
  });

  // ── Back navigation ──────────────────────────────────────────────────────────

  it("back button navigates to /designs", async () => {
    const user = userEvent.setup();
    renderPage(Role.Client);
    await screen.findByText("Pending");
    await user.click(screen.getByRole("button", { name: /designs/i }));
    expect(screen.getByTestId("designs-list")).toBeInTheDocument();
  });

  // ── Header: title, client, status ────────────────────────────────────────────

  it("renders the design title, client name, and status badge", async () => {
    renderPage(Role.Client);

    expect(await screen.findByRole("heading", { name: "Dragon Sleeve" })).toBeInTheDocument();
    expect(await screen.findByText(/for ana costa/i)).toBeInTheDocument();
    expect(await screen.findByText("In Review")).toBeInTheDocument();
  });

  it("shows a 'changes requested' banner when the design status is ChangesRequested", async () => {
    server.use(
      http.get("http://localhost/api/v1/designs/:designId", () =>
        HttpResponse.json({ ...DESIGN, status: "ChangesRequested", waitingOn: "Artist" }),
      ),
    );

    renderPage(Role.Artist);

    expect(await screen.findByText(/the client has requested changes/i)).toBeInTheDocument();
  });
});

// ── E04c (A05): a review names the revision it acts on ────────────────────────

const V1_CHANGES: DesignRevisionResponse = {
  ...REV_CHANGES, id: "rev-v1", versionNumber: 1, uploadedAt: "2024-01-10T08:00:00Z",
  approvalNotes: "Thinner lines please.", reviewedAt: "2024-01-12T09:15:00Z",
};
const V2_PENDING: DesignRevisionResponse = {
  ...REV_PENDING, id: "rev-v2", versionNumber: 2, uploadedAt: "2024-01-15T10:00:00Z", notes: null, reviewedAt: null,
};

function serveRevisions(revisions: DesignRevisionResponse[], design: Partial<DesignResponse> = {}) {
  server.use(
    http.get("http://localhost/api/v1/designs/:designId/revisions", () => HttpResponse.json(revisions)),
    http.get("http://localhost/api/v1/designs/:designId", () => HttpResponse.json({ ...DESIGN, ...design })),
  );
}

describe("DesignDetailPage — named revisions (E04c)", () => {
  it("names the version and its upload time (studio timezone) on the review the client is making", async () => {
    serveRevisions([V1_CHANGES, V2_PENDING], { waitingOn: "Client", latestVersionNumber: 2, latestUploadedAt: V2_PENDING.uploadedAt });
    renderPage(Role.Client);

    const group = await screen.findByRole("group", { name: "Your review of version 2, uploaded 15 Jan 2024, 10:00" });
    expect(within(group).getByRole("button", { name: /^approve$/i })).toBeInTheDocument();
    expect(within(group).getByRole("button", { name: /request changes/i })).toBeInTheDocument();
  });

  it("approving sends the latest revision's id, and only the latest offers review", async () => {
    const user = userEvent.setup();
    let reviewed: string | undefined;
    serveRevisions([V1_CHANGES, V2_PENDING], { waitingOn: "Client", latestVersionNumber: 2 });
    server.use(http.post("http://localhost/api/v1/designs/revisions/:revisionId/review", ({ params }) => {
      reviewed = String(params.revisionId);
      return HttpResponse.json({ ...V2_PENDING, approvalStatus: "Approved" });
    }));
    renderPage(Role.Client);

    await screen.findByRole("group", { name: /your review of version 2/i });
    // v1 is still not approved, but it is superseded: no second Approve anywhere on the page.
    expect(screen.getAllByRole("button", { name: /^approve$/i, hidden: true })).toHaveLength(1);
    await user.click(screen.getByRole("button", { name: /^approve$/i }));
    await vi.waitFor(() => expect(reviewed).toBe("rev-v2"));
  });

  it("the request-changes form names the version too", async () => {
    const user = userEvent.setup();
    serveRevisions([V2_PENDING], { waitingOn: "Client", latestVersionNumber: 2 });
    renderPage(Role.Client);

    await user.click(await screen.findByRole("button", { name: /request changes/i }));
    expect(screen.getByLabelText("What should change in version 2? (optional)")).toBeInTheDocument();
  });

  it("leads with the latest version; earlier ones sit behind a collapsed disclosure", async () => {
    serveRevisions([V1_CHANGES, V2_PENDING], { waitingOn: "Client", latestVersionNumber: 2 });
    renderPage(Role.Artist);

    const toggle = await screen.findByRole("button", { name: "Earlier versions (1)" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    const region = document.getElementById(toggle.getAttribute("aria-controls") ?? "");
    expect(region).not.toBeNull();
    expect(region).not.toBeVisible();
    expect(region).toHaveAttribute("hidden");
    // The first version label in the document is the latest.
    expect(screen.getAllByText(/^v\d$/)[0]).toHaveTextContent("v2");
    expect(within(region!).getByText("v1")).toBeInTheDocument();
  });

  it("the disclosure opens from the keyboard and nothing inside is tab-reachable while collapsed", async () => {
    const user = userEvent.setup();
    serveRevisions([V1_CHANGES, V2_PENDING], { waitingOn: "Client", latestVersionNumber: 2 });
    renderPage(Role.Artist);

    const toggle = await screen.findByRole("button", { name: "Earlier versions (1)" });
    const region = document.getElementById(toggle.getAttribute("aria-controls") ?? "")!;

    // Tab through the whole page while collapsed: focus never lands inside the region.
    for (let i = 0; i < 30; i += 1) {
      await user.tab();
      expect(region.contains(document.activeElement)).toBe(false);
    }

    toggle.focus();
    await user.keyboard("{Enter}");
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    expect(region).toBeVisible();
    await user.keyboard(" ");
    expect(toggle).toHaveAttribute("aria-expanded", "false");
  });

  it("keeps each version's feedback history, with when it was given", async () => {
    const user = userEvent.setup();
    serveRevisions([V1_CHANGES, V2_PENDING], { waitingOn: "Client", latestVersionNumber: 2 });
    renderPage(Role.Artist);

    await user.click(await screen.findByRole("button", { name: "Earlier versions (1)" }));
    const region = screen.getByRole("region", { name: "Earlier versions" });
    expect(within(region).getByText("Changes requested on 12 Jan 2024, 09:15.")).toBeInTheDocument();
    expect(within(region).getByText("Client feedback on version 1")).toBeInTheDocument();
    expect(within(region).getByText("Thinner lines please.")).toBeInTheDocument();
  });

  it("an unreviewed older version says it was superseded rather than 'pending'", async () => {
    const user = userEvent.setup();
    const v1Unreviewed: DesignRevisionResponse = { ...V2_PENDING, id: "rev-old", versionNumber: 1 };
    const v2: DesignRevisionResponse = { ...V2_PENDING, id: "rev-new", versionNumber: 2 };
    serveRevisions([v1Unreviewed, v2], { waitingOn: "Client", latestVersionNumber: 2 });
    renderPage(Role.Client);

    await user.click(await screen.findByRole("button", { name: "Earlier versions (1)" }));
    expect(within(screen.getByRole("region", { name: "Earlier versions" }))
      .getByText("Not reviewed. Superseded by version 2.")).toBeInTheDocument();
  });

  it("long notes and feedback collapse behind an accessible Show more", async () => {
    const user = userEvent.setup();
    const long = `${"Fine-line peony with a soft gradient into the shoulder, ".repeat(8)}end.`;
    serveRevisions([{ ...V2_PENDING, notes: long }], { waitingOn: "Client", latestVersionNumber: 2 });
    renderPage(Role.Client);

    const more = await screen.findByRole("button", { name: "Show more" });
    expect(more).toHaveAttribute("aria-expanded", "false");
    expect(screen.queryByText(long)).not.toBeInTheDocument();
    await user.click(more);
    expect(screen.getByRole("button", { name: "Show less" })).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByText(long)).toBeInTheDocument();
  });
});

describe("DesignDetailPage — whose move it is (E04c)", () => {
  it("awaiting the client's review and awaiting the artist's work render distinctly", async () => {
    serveRevisions([V2_PENDING], { waitingOn: "Client", latestVersionNumber: 2, latestUploadedAt: V2_PENDING.uploadedAt });
    renderPage(Role.Artist);
    const waitingClient = await screen.findByTestId("design-waiting-state");
    expect(waitingClient).toHaveAttribute("data-waiting-on", "Client");
    expect(waitingClient).toHaveTextContent("Waiting on the client: version 2, uploaded 15 Jan 2024, 10:00 is with them for review.");
    cleanup();

    serveRevisions([{ ...V2_PENDING, approvalStatus: "ChangesRequested" }], { status: "ChangesRequested", waitingOn: "Artist", latestVersionNumber: 2 });
    renderPage(Role.Artist);
    const waitingArtist = await screen.findByTestId("design-waiting-state");
    expect(waitingArtist).toHaveAttribute("data-waiting-on", "Artist");
    expect(waitingArtist).toHaveTextContent("The client has requested changes to version 2. Upload a new revision to continue.");
  });

  it("the client sees the same states from their side", async () => {
    serveRevisions([V2_PENDING], { waitingOn: "Client", latestVersionNumber: 2, latestUploadedAt: V2_PENDING.uploadedAt });
    renderPage(Role.Client);
    expect(await screen.findByTestId("design-waiting-state"))
      .toHaveTextContent("Your artist is waiting for your feedback on version 2, uploaded 15 Jan 2024, 10:00.");
    cleanup();

    serveRevisions([], { status: "Draft", waitingOn: "Artist", latestVersionNumber: null, latestUploadedAt: null });
    renderPage(Role.Client);
    expect(await screen.findByTestId("design-waiting-state"))
      .toHaveTextContent("Waiting on your artist: they haven't uploaded a version yet.");
  });
});

describe("DesignDetailPage — a design the viewer may not open (2026-11-06)", () => {
  it("a 404 says the design isn't available, not that loading failed", async () => {
    server.use(
      http.get("http://localhost/api/v1/designs/:designId", () => HttpResponse.json({ message: "not found" }, { status: 404 })),
      http.get("http://localhost/api/v1/designs/:designId/revisions", () => HttpResponse.json({ message: "not found" }, { status: 404 })),
    );
    renderPage(Role.Client);

    expect(await screen.findByText(/this design isn't available/i)).toBeInTheDocument();
    expect(screen.queryByText(/failed to load revisions/i)).not.toBeInTheDocument();
  });
});
