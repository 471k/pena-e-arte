import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { render, screen, fireEvent, act } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";

import { ReviewSection } from "@/features/public/components/ReviewSection";
import type { ReviewResponse } from "@/features/public/publicApi";

// ── Mocks ──────────────────────────────────────────────────────────────────────

const mockCreateArtistReview         = vi.fn();
const mockCreateStudioReview         = vi.fn();
const mockCreatePortfolioImageReview = vi.fn();

const mockArtistReviewsResult = { data: [] as ReviewResponse[], isLoading: false };

vi.mock("@/features/public/publicApi", () => ({
  useGetArtistReviewsQuery:               () => mockArtistReviewsResult,
  useGetStudioReviewsQuery:               () => ({ data: [] as ReviewResponse[], isLoading: false }),
  useGetPortfolioImageReviewsQuery:       () => ({ data: [] as ReviewResponse[], isLoading: false }),
  useCreateArtistReviewMutation:         () => [mockCreateArtistReview,         { isLoading: false }],
  useCreateStudioReviewMutation:         () => [mockCreateStudioReview,         { isLoading: false }],
  useCreatePortfolioImageReviewMutation: () => [mockCreatePortfolioImageReview, { isLoading: false }],
}));

// ── Helpers ────────────────────────────────────────────────────────────────────

function renderSection(
  token:   string | null = "test-token",
  target:  "studio" | "artist" | "tattoo" = "artist",
  imageId?: string,
) {
  render(
    <MemoryRouter>
      <ReviewSection slug="maria-silva" target={target} token={token} imageId={imageId} />
    </MemoryRouter>,
  );
}

// ── Tests ──────────────────────────────────────────────────────────────────────

beforeEach(() => {
  vi.clearAllMocks();
  mockArtistReviewsResult.data = [];
  mockArtistReviewsResult.isLoading = false;
});

describe("ReviewSection — heading", () => {
  it("renders Reviews heading with text-base font-semibold", () => {
    renderSection();
    const heading = screen.getByRole("heading", { name: /reviews/i });
    expect(heading).toBeInTheDocument();
    expect(heading.classList.contains("text-base")).toBe(true);
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

    const ratingButtons = screen.getAllByRole("radio", { name: /rate \d of 5/i });
    fireEvent.click(ratingButtons[4]);

    const textarea = screen.getByRole("textbox", { name: /write a review/i });
    fireEvent.change(textarea, { target: { value: "Excellent work, highly recommend!" } });

    const submitBtn = screen.getByRole("button", { name: /post review/i });
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

describe("ReviewSection — tattoo target", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    mockCreatePortfolioImageReview.mockReturnValue({
      unwrap: () => Promise.resolve(),
    });
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it("renders review form for tattoo target when authenticated", () => {
    renderSection("test-token", "tattoo", "img-001");
    expect(screen.getByRole("textbox", { name: /write a review/i })).toBeInTheDocument();
  });

  it("shows sign-in gate for tattoo target when unauthenticated", () => {
    renderSection(null, "tattoo", "img-001");
    expect(screen.getByText(/sign in to share your experience with this tattoo/i)).toBeInTheDocument();
    expect(screen.queryByRole("textbox", { name: /write a review/i })).not.toBeInTheDocument();
  });

  it("calls createPortfolioImageReview on submission", async () => {
    renderSection("test-token", "tattoo", "img-001");

    const ratingButtons = screen.getAllByRole("radio", { name: /rate \d of 5/i });
    fireEvent.click(ratingButtons[3]);

    const textarea = screen.getByRole("textbox", { name: /write a review/i });
    fireEvent.change(textarea, { target: { value: "Beautiful tattoo work!" } });

    const submitBtn = screen.getByRole("button", { name: /post review/i });
    await act(async () => {
      fireEvent.click(submitBtn);
    });

    expect(mockCreatePortfolioImageReview).toHaveBeenCalledWith({
      imageId: "img-001",
      rating: 4,
      body: "Beautiful tattoo work!",
    });
    expect(mockCreateArtistReview).not.toHaveBeenCalled();
    expect(mockCreateStudioReview).not.toHaveBeenCalled();
  });
});

describe("ReviewSection — Post Review button state", () => {
  it("'Post Review' button is disabled when rating is 0", () => {
    renderSection();
    const btn = screen.getByRole("button", { name: /post review/i });
    expect(btn).toBeDisabled();
  });

  it("'Post Review' button enables after selecting a star", async () => {
    const user = userEvent.setup();
    renderSection();
    const btn = screen.getByRole("button", { name: /post review/i });
    expect(btn).toBeDisabled();
    await user.click(screen.getByRole("radio", { name: /rate 3 of 5/i }));
    expect(btn).not.toBeDisabled();
  });

  it("pressing Enter in textarea without a rating shows an error and does not submit", async () => {
    const user = userEvent.setup();
    renderSection();
    const textarea = screen.getByRole("textbox", { name: /write a review/i });
    await user.type(textarea, "This is a long enough review body");
    await user.keyboard("{Enter}");
    expect(await screen.findByRole("alert")).toHaveTextContent(/please select a star rating/i);
    expect(mockCreateArtistReview).not.toHaveBeenCalled();
  });
});

describe("ReviewSection — Verified client badge", () => {
  it("shows 'Verified client' badge when isVerifiedBooking is true", () => {
    mockArtistReviewsResult.data = [{
      id: "r-1",
      authorName: "Ana Costa",
      rating: 5,
      body: "Fantastic work",
      createdAt: "2026-06-01T00:00:00Z",
      isVerifiedBooking: true,
    }];
    renderSection();
    expect(screen.getByText(/verified client/i)).toBeInTheDocument();
  });
});

describe("ReviewSection — form DOM order", () => {
  it("the write form appears after the review list in DOM order", () => {
    renderSection();
    const section = screen.getByRole("region", { name: /reviews/i });
    const children = Array.from(section.children);
    const headingIdx = children.findIndex((el) =>
      el.querySelector("[id='reviews-heading']") !== null
    );
    const formIdx = children.findIndex((el) =>
      el.querySelector("[aria-label='Write a review']") !== null ||
      el.textContent?.includes("Write a review")
    );
    expect(formIdx).toBeGreaterThan(headingIdx);
  });
});
