import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, waitFor, within, fireEvent } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";
import { toast } from "sonner";

import authReducer from "@/features/auth/authSlice";
import { feedbackApi } from "@/features/feedback/feedbackApi";
import { filesApi } from "@/shared/api/filesApi";
import { FeedbackDialog } from "@/features/feedback/components/FeedbackDialog";
import type { FeedbackReportResponse } from "@/features/feedback/feedback.types";

vi.mock("sonner", () => ({ toast: { error: vi.fn(), success: vi.fn() } }));

const CREATED_REPORT: FeedbackReportResponse = {
  id:            "fb-0001",
  type:          "BugReport",
  title:         "Broken button",
  body:          "The submit button does nothing on Safari.",
  status:        "Open",
  studioName:    "Test Studio",
  submitterRole: "artist",
  issuerNote:    null,
  createdAt:     "2026-07-01T00:00:00.000Z",
  resolvedAt:    null,
};

// The presigned upload URL must be resolvable by MSW (i.e., absolute localhost URL).
const PRESIGN_UPLOAD_URL = "http://localhost/r2-upload";
const PRESIGN_PUBLIC_URL = "https://cdn.example.com/feedback/screenshot.png";

const server = setupServer(
  http.post("http://localhost/api/v1/feedback", () => HttpResponse.json(CREATED_REPORT), ),
  http.post("http://localhost/api/v1/files/presign", () =>
    HttpResponse.json({ uploadUrl: PRESIGN_UPLOAD_URL, publicUrl: PRESIGN_PUBLIC_URL })),
  http.put(PRESIGN_UPLOAD_URL, () => new HttpResponse(null, { status: 200 })),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => server.resetHandlers());
afterAll(() => server.close());

function makeStore() {
  return configureStore({
    reducer: {
      auth:                       authReducer,
      [feedbackApi.reducerPath]:  feedbackApi.reducer,
      [filesApi.reducerPath]:     filesApi.reducer,
    },
    middleware: (gd) => gd().concat(feedbackApi.middleware, filesApi.middleware),
    preloadedState: {
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      auth: { user: { id: "u1", email: "artist@test.com" }, token: "fake", tenantId: "t1", role: "artist" } as any,
    },
  });
}

function getFileInput() {
  return document.querySelector<HTMLInputElement>("input[type=file]")!;
}

function makePng(name = "screenshot.png") {
  return new File(["img-bytes"], name, { type: "image/png" });
}

function renderDialog(onOpenChange = vi.fn()) {
  const store = makeStore();
  render(
    <Provider store={store}>
      <FeedbackDialog open onOpenChange={onOpenChange} />
    </Provider>,
  );
  return { onOpenChange };
}

describe("FeedbackDialog", () => {
  it("renders with type selector, title input, body textarea, cancel + submit buttons", () => {
    renderDialog();

    expect(screen.getByLabelText(/type/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/title/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/description/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /cancel/i })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: /send feedback/i })).toBeInTheDocument();
  });

  it("type selector defaults to Bug Report", () => {
    renderDialog();
    expect(screen.getByLabelText(/type/i)).toHaveTextContent(/bug report/i);
  });

  it("empty title shows validation error on submit", async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/description/i), "A description with enough characters.");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    expect(await screen.findByText(/title is required/i)).toBeInTheDocument();
  });

  it("body under 10 chars shows validation error on submit", async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/title/i), "Short body test");
    await user.type(screen.getByLabelText(/description/i), "short");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    expect(await screen.findByText(/at least 10 characters/i)).toBeInTheDocument();
  });

  it("valid form submission calls submitFeedback mutation", async () => {
    const user = userEvent.setup();
    let captured: unknown = null;
    server.use(
      http.post("http://localhost/api/v1/feedback", async ({ request }) => {
        captured = await request.json();
        return HttpResponse.json(CREATED_REPORT);
      }),
    );
    renderDialog();

    await user.type(screen.getByLabelText(/title/i), "Broken button");
    await user.type(screen.getByLabelText(/description/i), "The submit button does nothing on Safari.");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    await waitFor(() => expect(captured).toMatchObject({ title: "Broken button" }), { timeout: 10000 });
  }, 15000);

  it("shows a spinner on the submit button while loading", async () => {
    const user = userEvent.setup();
    // Never resolves — keeps the mutation pending for the life of the test,
    // so the loading assertion below is not a race against a timed response.
    server.use(
      http.post("http://localhost/api/v1/feedback", () => new Promise(() => {})),
    );
    renderDialog();

    await user.type(screen.getByLabelText(/title/i), "Broken button");
    await user.type(screen.getByLabelText(/description/i), "The submit button does nothing on Safari.");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    await waitFor(() => expect(document.querySelector(".animate-spin")).toBeInTheDocument(), { timeout: 10000 });
  }, 15000);

  it("shows the thank-you confirmation view on success", async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/title/i), "Broken button");
    await user.type(screen.getByLabelText(/description/i), "The submit button does nothing on Safari.");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    expect(await screen.findByText(/thank you for your feedback/i)).toBeInTheDocument();
  });

  it("close button after success calls onOpenChange(false)", async () => {
    const user = userEvent.setup();
    const { onOpenChange } = renderDialog();

    await user.type(screen.getByLabelText(/title/i), "Broken button");
    await user.type(screen.getByLabelText(/description/i), "The submit button does nothing on Safari.");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    const thankYou = await screen.findByText(/thank you for your feedback/i);
    await user.click(within(thankYou.closest("div")!).getByRole("button", { name: /^close$/i }));

    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it("closing resets the form back to default state", async () => {
    const user = userEvent.setup();
    renderDialog();

    await user.type(screen.getByLabelText(/title/i), "Some draft title");
    await user.click(screen.getByRole("button", { name: /cancel/i }));

    expect(screen.getByLabelText(/title/i)).toHaveValue("");
  });

  it("renders the attachments dropzone", () => {
    renderDialog();
    expect(screen.getByText(/add a screenshot or short video/i)).toBeInTheDocument();
    expect(getFileInput()).toBeInTheDocument();
  });

  it("shows a file chip after picking an attachment", async () => {
    const user = userEvent.setup();
    renderDialog();
    await user.upload(getFileInput(), makePng());
    expect(await screen.findByText("screenshot.png")).toBeInTheDocument();
  });

  it("removes a file chip when its remove button is clicked", async () => {
    const user = userEvent.setup();
    renderDialog();
    await user.upload(getFileInput(), makePng());
    await screen.findByText("screenshot.png");
    await user.click(screen.getByRole("button", { name: /remove screenshot\.png/i }));
    expect(screen.queryByText("screenshot.png")).not.toBeInTheDocument();
  });

  it("shows an error for an unsupported file type", () => {
    renderDialog();
    const pdf = new File(["pdf"], "notes.pdf", { type: "application/pdf" });
    const input = getFileInput();
    Object.defineProperty(input, "files", { value: [pdf], configurable: true });
    fireEvent.change(input);
    expect(screen.getByText(/only jpeg\/png\/webp images or mp4\/webm\/mov videos/i)).toBeInTheDocument();
  });

  it("includes the uploaded attachment URL in the submit payload", async () => {
    const user = userEvent.setup();
    let captured: { attachmentUrls?: string[] } | null = null;
    server.use(
      http.post("http://localhost/api/v1/feedback", async ({ request }) => {
        captured = await request.json() as { attachmentUrls?: string[] };
        return HttpResponse.json(CREATED_REPORT);
      }),
    );
    renderDialog();

    await user.upload(getFileInput(), makePng());
    await waitFor(() => expect(screen.getByRole("button", { name: /send feedback/i })).not.toBeDisabled());

    await user.type(screen.getByLabelText(/title/i), "Broken button");
    await user.type(screen.getByLabelText(/description/i), "The submit button does nothing on Safari.");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    await waitFor(() => expect(captured).toEqual(
      expect.objectContaining({ attachmentUrls: [PRESIGN_PUBLIC_URL] }),
    ));
  });

  it("does not include attachmentUrls when no files were attached", async () => {
    const user = userEvent.setup();
    let captured: { attachmentUrls?: string[] } | null = null;
    server.use(
      http.post("http://localhost/api/v1/feedback", async ({ request }) => {
        captured = await request.json() as { attachmentUrls?: string[] };
        return HttpResponse.json(CREATED_REPORT);
      }),
    );
    renderDialog();

    await user.type(screen.getByLabelText(/title/i), "Broken button");
    await user.type(screen.getByLabelText(/description/i), "The submit button does nothing on Safari.");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    await waitFor(() => expect(captured).not.toBeNull());
    expect(captured!.attachmentUrls).toBeUndefined();
  });

  it("shows an error toast when the mutation fails", async () => {
    const user = userEvent.setup();
    server.use(
      http.post("http://localhost/api/v1/feedback", () => HttpResponse.json({ message: "fail" }, { status: 500 })),
    );
    renderDialog();

    await user.type(screen.getByLabelText(/title/i), "Broken button");
    await user.type(screen.getByLabelText(/description/i), "The submit button does nothing on Safari.");
    await user.click(screen.getByRole("button", { name: /send feedback/i }));

    await waitFor(() => expect(toast.error).toHaveBeenCalled());
  });
});
