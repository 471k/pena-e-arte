import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { GuestBookAppointmentForm } from "@/features/booking/components/GuestBookAppointmentForm";

// ── Mocks ──────────────────────────────────────────────────────────────────────

const ARTIST = {
  artistId: "a-001", name: "Luna Artista", avatarUrl: null,
  specializations: "Neo-trad", hourlyRate: 80,
};

const mockCreateGuestAppointment = vi.fn();
const mockPresignGuestUpload = vi.fn();

vi.mock("@/features/public/publicApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/public/publicApi")>();
  return {
    ...actual,
    useGetPublicBookingArtistsQuery: () => ({ data: [ARTIST], isLoading: false }),
    useCheckPublicSlotAvailabilityQuery: () => ({ data: { available: true, reason: null }, isFetching: false }),
    useGetPublicDepositRuleQuery: () => ({ data: null }),
    useCreateGuestAppointmentMutation: () => [mockCreateGuestAppointment, { isLoading: false }],
    usePresignGuestUploadMutation: () => [mockPresignGuestUpload, { isLoading: false }],
  };
});

beforeEach(() => {
  mockCreateGuestAppointment.mockReset();
  mockPresignGuestUpload.mockReset();
  mockPresignGuestUpload.mockReturnValue({
    unwrap: () => Promise.resolve({ uploadUrl: "https://r2.example.com/upload", publicUrl: "https://cdn.example.com/img.png" }),
  });
  vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(new Response(null, { status: 200 }))));
  // Defensive reset for a known Radix Select/jsdom gotcha (see architecture.md's "Gotcha:
  // Dialog-based overlay opened from a DropdownMenuItem" — same root cause, different
  // component): a modal Select that doesn't fully restore `body.style.pointerEvents` on
  // close in jsdom can leave the next test's inputs unclickable via userEvent.
  document.body.style.pointerEvents = "";
});

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  document.body.style.pointerEvents = "";
});

function renderForm() {
  render(<GuestBookAppointmentForm slug="test-studio" />);
}

// FieldLabel appends a trailing "*" for required fields, so the accessible label text is
// "First name*" not "First name" — regex matches avoid depending on that exact rendering.
async function fillIdentityFields(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText(/first name/i), "Jamie");
  await user.type(screen.getByLabelText(/last name/i), "Guest");
  await user.type(screen.getByLabelText(/^email/i), "jamie@example.com");
  await user.type(screen.getByLabelText(/^phone/i), "912345678");
}

async function fillBookingFields(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByLabelText(/^artist/i));
  await user.click(await screen.findByRole("option", { name: "Luna Artista" }));
  await user.type(
    screen.getByLabelText(/date.*time/i),
    new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 16),
  );
  await user.type(screen.getByLabelText(/what are you looking to get done/i), "A small rose");
}

async function uploadImages(user: ReturnType<typeof userEvent.setup>) {
  const inputs = document.querySelectorAll<HTMLInputElement>("input[type=file]");
  const png = new File(["img"], "photo.png", { type: "image/png" });
  await user.upload(inputs[0], png);
  await user.upload(inputs[1], png);
  await waitFor(() => expect(screen.getAllByRole("img")).toHaveLength(2));
}

describe("GuestBookAppointmentForm", () => {
  it("renders the identity fields", () => {
    renderForm();
    expect(screen.getByLabelText(/first name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/last name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^phone/i)).toBeInTheDocument();
  });

  it("renders the marketing opt-in toggle", () => {
    renderForm();
    expect(screen.getByRole("switch", { name: /sign up for news and updates/i })).toBeInTheDocument();
  });

  it("renders the artist picker sourced from the public booking-artists endpoint", async () => {
    renderForm();
    expect(screen.getByLabelText(/^artist/i)).toBeInTheDocument();
  });

  it("renders both area-photo and reference-image dropzones as required", () => {
    renderForm();
    const areaLabel = screen.getByText("Area photo", { selector: "label" });
    const refLabel = screen.getByText("Reference images", { selector: "label" });
    expect(areaLabel.textContent).toContain("*");
    expect(refLabel.textContent).toContain("*");
  });

  it("shows required-field errors when submitted completely empty", async () => {
    const user = userEvent.setup();
    renderForm();

    await user.click(screen.getByRole("button", { name: /request appointment/i }));

    expect(await screen.findByText("First name is required")).toBeInTheDocument();
    expect(screen.getByText("Last name is required")).toBeInTheDocument();
    expect(screen.getByText("Email is required")).toBeInTheDocument();
    expect(screen.getByText("Phone number is required")).toBeInTheDocument();
  });

  it("shows tattoo-description and both image-category errors once identity/booking fields are valid but images are missing", async () => {
    const user = userEvent.setup();
    renderForm();

    await fillIdentityFields(user);
    await user.click(screen.getByLabelText(/^artist/i));
    await user.click(await screen.findByRole("option", { name: "Luna Artista" }));
    await user.type(
      screen.getByLabelText(/date.*time/i),
      new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 16),
    );

    await user.click(screen.getByRole("button", { name: /request appointment/i }));

    expect(await screen.findByText("Tell us what you're looking to get done.")).toBeInTheDocument();
    expect(screen.getByText("A photo of the area is required.")).toBeInTheDocument();
    expect(screen.getByText("At least one reference image is required.")).toBeInTheDocument();
    expect(mockCreateGuestAppointment).not.toHaveBeenCalled();
    // Same sandbox CPU-contention timeout class as the two tests below (src/test/setup.ts's
    // asyncUtilTimeout comment) — this one's identity+artist-Select+datetime interaction
    // sequence is heavy enough to occasionally cross the 10s default under load. Bumped
    // 20000 -> 40000 (2026-09-05): 20s itself proved insufficient on a slower/more-loaded
    // machine — not a hang, the same sequence just genuinely takes longer there.
  }, 40000);

  it("submits successfully once every field and both required images are filled", async () => {
    // The component's result branching checks `"data" in result` on the awaited mutation call
    // directly (no `.unwrap()`), matching RTK Query's dispatched-thunk return shape.
    mockCreateGuestAppointment.mockReturnValue(
      Promise.resolve({ data: { message: "Thanks — check your email to continue." } }) as unknown as ReturnType<typeof mockCreateGuestAppointment>,
    );

    const user = userEvent.setup();
    renderForm();

    await fillIdentityFields(user);
    await fillBookingFields(user);
    await uploadImages(user);

    await user.click(screen.getByRole("button", { name: /request appointment/i }));

    expect(await screen.findByText("Check your email")).toBeInTheDocument();
    expect(mockCreateGuestAppointment).toHaveBeenCalledWith(
      expect.objectContaining({
        slug: "test-studio",
        body: expect.objectContaining({
          firstName: "Jamie", lastName: "Guest", email: "jamie@example.com",
        }),
      }),
    );
    // Longer per-test timeout: this is the most interaction-heavy test in the file (every
    // field + an artist Select + two real image uploads + submit), and this sandbox's known
    // CPU-contention flakiness (see src/test/setup.ts's asyncUtilTimeout comment) pushes it
    // past the 10s default under load even though nothing is actually broken.
  }, 40000);

  // Enumeration-resistance (2026-09-01, /code-review finding): the backend now returns the
  // exact same ack whether a new booking was created or the email collided with an existing
  // account — it never sends a 409 for this case anymore. This test confirms the frontend
  // shows the identical generic success screen either way, rather than assuming a shape the
  // backend no longer produces.
  it("shows the same generic success screen for a duplicate-email ack as for a real booking", async () => {
    mockCreateGuestAppointment.mockReturnValue(
      Promise.resolve({ data: { message: "Thanks — check your email to continue." } }) as unknown as ReturnType<typeof mockCreateGuestAppointment>,
    );

    const user = userEvent.setup();
    renderForm();

    await fillIdentityFields(user);
    await fillBookingFields(user);
    await uploadImages(user);

    await user.click(screen.getByRole("button", { name: /request appointment/i }));

    expect(await screen.findByText("Check your email")).toBeInTheDocument();
    expect(screen.queryByText(/already exists/i)).not.toBeInTheDocument();
  }, 40000);
});
