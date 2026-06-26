import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, act } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";

import { ReviewSection } from "@/features/public/components/ReviewSection";

// ── Mocks ──────────────────────────────────────────────────────────────────────

const mockCreateArtistReview = vi.fn();
const mockCreateStudioReview = vi.fn();

vi.mock("@/features/public/publicApi", () => ({
  useGetArtistReviewsQuery:      () => ({ data: [], isLoading: false }),
  useGetStudioReviewsQuery:      () => ({ data: [], isLoading: false }),
  useCreateArtistReviewMutation: () => [mockCreateArtistReview, { isLoading: false }],
  useCreateStudioReviewMutation: () => [mockCreateStudioReview, { isLoading: false }],
}));

// ── Helpers ────────────────────────────────────────────────────────────────────

function renderSection(token: string | null = "test-token") {
  render(
    <MemoryRouter>
      <ReviewSection slug="maria-silva" target="artist" token={token} />
    </MemoryRouter>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

describe("ReviewSection — heading", () => {
  it("renders Reviews heading with text-lg font-semibold", () => {
    renderSection();
    const heading = screen.getByRole("heading", { name: /reviews/i });
    expect(heading).toBeInTheDocument();
    expect(heading.classList.contains("text-lg")).toBe(true);
    expect(heading.classList.contains("font-semibold")).toBe(true);
  });
});

describe("ReviewSection — success auto-dismiss", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    mockCreateArtistReview.mockReturnValue({
      unwrap: () => Promise.resolve(),
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  async function submitReview() {
    renderSection();

    // Select rating (click 5th star button)
    const ratingButtons = screen.getAllByRole("button", { name: /rate \d out of 5/i });
    fireEvent.click(ratingButtons[4]);

    // Fill in review body
    const textarea = screen.getByRole("textbox", { name: /write a review/i });
    fireEvent.change(textarea, { target: { value: "Excellent work, highly recommend!" } });

    // Submit
    const submitBtn = screen.getByRole("button", { name: /submit review/i });
    await act(async () => {
      fireEvent.click(submitBtn);
    });
  }

  it("renders success message with role='status' after submission", async () => {
    await submitReview();
    const status = screen.getByRole("status");
    expect(status).toBeInTheDocument();
    expect(status).toHaveTextContent(/review submitted/i);
  });

  it("success message disappears after 4 seconds", async () => {
    await submitReview();
    expect(screen.getByRole("status")).toBeInTheDocument();

    act(() => {
      vi.advanceTimersByTime(4001);
    });

    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("success message uses muted green styling, not vivid green on black", async () => {
    await submitReview();
    const status = screen.getByRole("status");
    // Should use the muted container classes, not a bare <p> with text-green-600
    expect(status.tagName).toBe("DIV");
    expect(status.className).toContain("green-950");
  });
});

describe("ReviewSection — unauthenticated gate", () => {
  it("shows sign-in gate instead of form when unauthenticated", () => {
    renderSection(null);
    expect(
      screen.getByText(/sign in to share your experience/i),
    ).toBeInTheDocument();
    expect(
      screen.getByRole("link", { name: /sign in to leave a review/i }),
    ).toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: /write a review/i })).not.toBeInTheDocument();
  });

  it("shows the form when authenticated", () => {
    renderSection("test-token");
    expect(screen.getByRole("textbox", { name: /write a review/i })).toBeInTheDocument();
    expect(screen.queryByText(/sign in to share your experience/i)).not.toBeInTheDocument();
  });
});
