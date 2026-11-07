import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { GuestBookAppointmentForm } from "@/features/booking/components/GuestBookAppointmentForm";

// ── Mocks ──────────────────────────────────────────────────────────────────────

const ARTIST = {
  artistId: "a-001", name: "Luna Artista", avatarUrl: null,
  specializations: ["neo-traditional"], hourlyRate: 80,
};

let mockStudio: { currency: string; countryCode?: string } = { currency: "EUR" };

const mockCreateGuestAppointment = vi.fn();
const mockPresignGuestUpload = vi.fn();

vi.mock("@/features/public/publicApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/public/publicApi")>();
  return {
    ...actual,
    useGetPublicBookingArtistsQuery: () => ({ data: [ARTIST], isLoading: false }),
    useCheckPublicSlotAvailabilityQuery: () => ({ data: { available: true, reason: null }, isFetching: false }),
    useGetPublicDepositRuleQuery: () => ({ data: null }),
    useGetPublicServicesQuery: () => ({ data: [] }),
    useGetPublicStudioQuery: () => ({ data: mockStudio }),
    useCreateGuestAppointmentMutation: () => [mockCreateGuestAppointment, { isLoading: false }],
    usePresignGuestUploadMutation: () => [mockPresignGuestUpload, { isLoading: false }],
  };
});

beforeEach(() => {
  mockStudio = { currency: "EUR" };
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
// These helpers enter each value with user.paste, not user.type: no test here asserts anything
// about the state between keystrokes, and typing ~45 characters one key at a time re-rendered the
// form (and the phone field's ~240-item calling-code picker) once per key. That work, not a small
// budget, is why these tests needed 40 s overrides (measured 2026-11-06: up to 29 s under
// coverage with 3 busy workers). The entered values and every assertion are unchanged.
// What is left of those overrides is explained at FULL_FORM_TIMEOUT below.
// PhoneInput's key-by-key formatting is covered by phone-input.test.tsx.
async function enter(user: ReturnType<typeof userEvent.setup>, field: HTMLElement, value: string) {
  await user.click(field);
  await user.paste(value);
}

async function fillIdentityFields(user: ReturnType<typeof userEvent.setup>) {
  await enter(user, screen.getByLabelText(/first name/i), "Jamie");
  await enter(user, screen.getByLabelText(/last name/i), "Guest");
  await enter(user, screen.getByLabelText(/^email/i), "jamie@example.com");
  // Albanian mobile: the phone field defaults to Albania when the studio country is unknown.
  await enter(user, screen.getByLabelText(/^phone/i), "691234567");
}

async function fillBookingFields(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByLabelText(/^artist/i));
  await user.click(await screen.findByRole("option", { name: "Luna Artista" }));
  await user.type(
    screen.getByLabelText(/date.*time/i),
    new Date(Date.now() + 7 * 86_400_000).toISOString().slice(0, 16),
  );
  await enter(user, screen.getByLabelText(/what are you looking to get done/i), "A small rose");
}

async function uploadImages(user: ReturnType<typeof userEvent.setup>) {
  const inputs = document.querySelectorAll<HTMLInputElement>("input[type=file]");
  const png = new File(["img"], "photo.png", { type: "image/png" });
  await user.upload(inputs[0], png);
  await user.upload(inputs[1], png);
  await waitFor(() => expect(screen.getAllByRole("img")).toHaveLength(2));
}

// The five tests that fill the whole form keep a per-test budget, halved from 40 s on 2026-11-07.
// Since the calling-code picker's items are memoized, typing no longer re-renders them, and these
// tests paste their text anyway. What is left is about 3.7 s isolated (measured stage by stage:
// mount 0.4-1 s, opening and picking the artist ~0.9 s and the style ~1.1 s, the date typed key by
// key ~0.4 s, two uploads ~0.35 s). The two Select picks are mostly jsdom role queries scanning the
// whole form, including the hidden native <select> that Radix keeps for each picker in a form.
// Under the full suite with coverage on a 2-core laptop that reached 10.1-10.4 s, just over the
// 10 s default, so the override stays at a lower value rather than coming off.
const FULL_FORM_TIMEOUT = 20_000;

describe("GuestBookAppointmentForm", () => {
  it("renders the identity fields", () => {
    renderForm();
    expect(screen.getByLabelText(/first name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/last name/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^email/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/^phone/i)).toBeInTheDocument();
  });

  it("defaults the phone prefix to the studio's own country, not always Albania", () => {
    mockStudio = { currency: "EUR", countryCode: "PT" };
    renderForm();
    expect(screen.getByRole("combobox", { name: "Country code" })).toHaveTextContent("+351");
  });

  it("falls back to Albania when the studio country is missing or unsupported", () => {
    mockStudio = { currency: "EUR", countryCode: "" };
    renderForm();
    expect(screen.getByRole("combobox", { name: "Country code" })).toHaveTextContent("+355");
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
  }, FULL_FORM_TIMEOUT);

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
  }, FULL_FORM_TIMEOUT);

  it("includes the selected style in the booking payload, or null when unset", async () => {
    mockCreateGuestAppointment.mockReturnValue(
      Promise.resolve({ data: { message: "Thanks — check your email to continue." } }) as unknown as ReturnType<typeof mockCreateGuestAppointment>,
    );

    const user = userEvent.setup();
    renderForm();

    await fillIdentityFields(user);
    await fillBookingFields(user);
    await user.click(screen.getByLabelText(/style \(optional\)/i));
    await user.click(await screen.findByRole("option", { name: "Japanese" }));
    await uploadImages(user);

    await user.click(screen.getByRole("button", { name: /request appointment/i }));

    await screen.findByText("Check your email");
    expect(mockCreateGuestAppointment).toHaveBeenCalledWith(
      expect.objectContaining({
        body: expect.objectContaining({
          booking: expect.objectContaining({ style: "japanese" }),
        }),
      }),
    );
  }, FULL_FORM_TIMEOUT);

  it("sends style: null when no style is selected", async () => {
    mockCreateGuestAppointment.mockReturnValue(
      Promise.resolve({ data: { message: "Thanks — check your email to continue." } }) as unknown as ReturnType<typeof mockCreateGuestAppointment>,
    );

    const user = userEvent.setup();
    renderForm();

    await fillIdentityFields(user);
    await fillBookingFields(user);
    await uploadImages(user);

    await user.click(screen.getByRole("button", { name: /request appointment/i }));

    await screen.findByText("Check your email");
    expect(mockCreateGuestAppointment).toHaveBeenCalledWith(
      expect.objectContaining({
        body: expect.objectContaining({
          booking: expect.objectContaining({ style: null }),
        }),
      }),
    );
  }, FULL_FORM_TIMEOUT);

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
  }, FULL_FORM_TIMEOUT);
});
