import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { render, screen, cleanup, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { GuestBookAppointmentForm } from "@/features/booking/components/GuestBookAppointmentForm";
import { toStudioInputValue } from "@/shared/utils/studioDay";

// ── Mocks ──────────────────────────────────────────────────────────────────────

const ARTIST = {
  artistId: "a-001", name: "Luna Artista", avatarUrl: null,
  specializations: ["neo-traditional"], hourlyRate: 80,
};

// The studio is in Tokyo, which is never the test runner's zone: a value read in the browser zone would
// submit a different instant, so these tests catch it (S01).
const STUDIO_TZ = "Asia/Tokyo";
let mockStudio: { currency: string; countryCode?: string; timezone?: string } = { currency: "EUR", timezone: STUDIO_TZ };

const mockCreateGuestAppointment = vi.fn();
const mockPresignGuestUpload = vi.fn();
// The slot list (C03). Default: nothing offered, so the existing tests type the time by hand as before.
const mockSlotsRefetch = vi.fn();
interface SlotsResult {
  data?: { from: string; to: string; durationMinutes: number; stepMinutes: number; days: { date: string; starts: string[] }[]; nextAvailable: string | null };
  isFetching: boolean;
  isError: boolean;
  refetch: () => unknown;
}
let mockSlots: (args: unknown) => SlotsResult = () => ({ data: undefined, isFetching: false, isError: false, refetch: mockSlotsRefetch });

vi.mock("@/features/public/publicApi", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/features/public/publicApi")>();
  return {
    ...actual,
    useGetPublicBookingArtistsQuery: () => ({ data: [ARTIST], isLoading: false }),
    useCheckPublicSlotAvailabilityQuery: () => ({ data: { available: true, reason: null }, isFetching: false }),
    useGetPublicAvailableSlotsQuery: (args: unknown) => mockSlots(args),
    useGetPublicDepositRuleQuery: () => ({ data: null }),
    useGetPublicServicesQuery: () => ({ data: [] }),
    useGetPublicStudioQuery: () => ({ data: mockStudio }),
    useCreateGuestAppointmentMutation: () => [mockCreateGuestAppointment, { isLoading: false }],
    usePresignGuestUploadMutation: () => [mockPresignGuestUpload, { isLoading: false }],
  };
});

beforeEach(() => {
  mockStudio = { currency: "EUR", timezone: STUDIO_TZ };
  mockCreateGuestAppointment.mockReset();
  mockPresignGuestUpload.mockReset();
  mockSlotsRefetch.mockReset();
  mockSlots = () => ({ data: undefined, isFetching: false, isError: false, refetch: mockSlotsRefetch });
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

  it("C09: arriving from a waitlist claim prefills the offered slot and sends the claim token", async () => {
    mockCreateGuestAppointment.mockReturnValue(
      Promise.resolve({ data: { message: "Thanks — check your email to continue." } }) as unknown as ReturnType<typeof mockCreateGuestAppointment>,
    );
    const slotStart = new Date(Date.UTC(2030, 2, 5, 13, 0)).toISOString();

    const user = userEvent.setup();
    render(<GuestBookAppointmentForm slug="test-studio"
      waitlistClaim={{ token: "claim-tok", artistId: ARTIST.artistId, slotStart, durationMinutes: 90 }} />);

    expect(screen.getByRole("note")).toHaveTextContent(/isn.t held for you: whoever books it first gets it/);
    // Prefilled on the studio's wall clock (13:00 UTC is 22:00 in Tokyo), and submitted back as the same instant.
    expect(screen.getByLabelText(/date.*time/i)).toHaveValue(toStudioInputValue(new Date(slotStart), STUDIO_TZ));
    expect(screen.getByLabelText(/date.*time/i)).toHaveValue("2030-03-05T22:00");

    await fillIdentityFields(user);
    await enter(user, screen.getByLabelText(/what are you looking to get done/i), "A small rose");
    await uploadImages(user);
    await user.click(screen.getByRole("button", { name: /request appointment/i }));

    await screen.findByText("Check your email");
    expect(mockCreateGuestAppointment).toHaveBeenCalledWith(
      expect.objectContaining({
        body: expect.objectContaining({
          booking: expect.objectContaining({
            artistId: ARTIST.artistId, date: slotStart, durationMinutes: 90, waitlistClaimToken: "claim-tok",
          }),
        }),
      }),
    );
  }, FULL_FORM_TIMEOUT);

  it("S01: the time is read on the studio's clock — 22:00 picked for a Tokyo studio books 13:00 UTC", async () => {
    mockCreateGuestAppointment.mockReturnValue(
      Promise.resolve({ data: { message: "Thanks — check your email to continue." } }) as unknown as ReturnType<typeof mockCreateGuestAppointment>,
    );
    const user = userEvent.setup();
    renderForm();
    expect(screen.getByText(/studio time, Asia\/Tokyo/)).toBeInTheDocument();

    await fillIdentityFields(user);
    await user.click(screen.getByLabelText(/^artist/i));
    await user.click(await screen.findByRole("option", { name: "Luna Artista" }));
    await user.type(screen.getByLabelText(/date.*time/i), "2030-03-05T22:00");
    await enter(user, screen.getByLabelText(/what are you looking to get done/i), "A small rose");
    await uploadImages(user);
    await user.click(screen.getByRole("button", { name: /request appointment/i }));

    await screen.findByText("Check your email");
    expect(mockCreateGuestAppointment).toHaveBeenCalledWith(expect.objectContaining({
      body: expect.objectContaining({ booking: expect.objectContaining({ date: "2030-03-05T13:00:00.000Z" }) }),
    }));
  }, FULL_FORM_TIMEOUT);

  it("S01: while the studio's zone is unknown nothing is sent — the form says why", async () => {
    mockStudio = { currency: "EUR" };
    const user = userEvent.setup();
    renderForm();
    expect(screen.getByText(/Loading the studio.s time zone/)).toBeInTheDocument();

    await fillIdentityFields(user);
    await fillBookingFields(user);
    await uploadImages(user);
    await user.click(screen.getByRole("button", { name: /request appointment/i }));

    expect(await screen.findByText(/Still loading the studio.s time zone/)).toBeInTheDocument();
    expect(mockCreateGuestAppointment).not.toHaveBeenCalled();
  }, FULL_FORM_TIMEOUT);

  it("S01: a time the clocks skip is refused, not shifted", async () => {
    mockStudio = { currency: "EUR", timezone: "Europe/Paris" };
    const user = userEvent.setup();
    renderForm();

    await fillIdentityFields(user);
    await user.click(screen.getByLabelText(/^artist/i));
    await user.click(await screen.findByRole("option", { name: "Luna Artista" }));
    await user.type(screen.getByLabelText(/date.*time/i), "2030-03-31T02:30");
    await enter(user, screen.getByLabelText(/what are you looking to get done/i), "A small rose");
    await uploadImages(user);
    await user.click(screen.getByRole("button", { name: /request appointment/i }));

    expect((await screen.findAllByText(/the clocks go forward/)).length).toBeGreaterThanOrEqual(1);
    expect(mockCreateGuestAppointment).not.toHaveBeenCalled();
  }, FULL_FORM_TIMEOUT);

  it("sends no claim token on an ordinary visit", async () => {
    mockCreateGuestAppointment.mockReturnValue(
      Promise.resolve({ data: { message: "Thanks — check your email to continue." } }) as unknown as ReturnType<typeof mockCreateGuestAppointment>,
    );
    const user = userEvent.setup();
    renderForm();

    expect(screen.queryByRole("note")).not.toBeInTheDocument();
    await fillIdentityFields(user);
    await fillBookingFields(user);
    await uploadImages(user);
    await user.click(screen.getByRole("button", { name: /request appointment/i }));

    await screen.findByText("Check your email");
    expect(mockCreateGuestAppointment).toHaveBeenCalledWith(
      expect.objectContaining({
        body: expect.objectContaining({ booking: expect.objectContaining({ waitlistClaimToken: null }) }),
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

// ── The slot list (C03): a preview, not a reservation ──────────────────────────────────────────

/** A week of days from the request's own `from`, with 10:00 and 13:00 Tokyo open on the third day. */
function weekWithTwoOpenTimes(args: unknown): SlotsResult {
  // RTK Query's skipToken (no artist chosen yet) carries no range: nothing to list.
  if (typeof args !== "object" || args === null) return { data: undefined, isFetching: false, isError: false, refetch: mockSlotsRefetch };
  const { from } = args as { from: string };
  const start = new Date(`${from}T00:00:00Z`);
  const days = Array.from({ length: 7 }, (_, i) => {
    const date = new Date(start.getTime() + i * 86_400_000).toISOString().slice(0, 10);
    // Tokyo is UTC+9 all year: 10:00 there is 01:00 UTC on the same date, 13:00 is 04:00 UTC.
    return { date, starts: i === 2 ? [`${date}T01:00:00.000Z`, `${date}T04:00:00.000Z`] : [] };
  });
  return {
    data: { from, to: days[6].date, durationMinutes: 60, stepMinutes: 60, days, nextAvailable: days[2].starts[0] },
    isFetching: false, isError: false, refetch: mockSlotsRefetch,
  };
}

const openDay = () => {
  const today = new Date(new Date().toLocaleString("en-US", { timeZone: STUDIO_TZ }));
  return new Date(Date.UTC(today.getFullYear(), today.getMonth(), today.getDate() + 2)).toISOString().slice(0, 10);
};

describe("GuestBookAppointmentForm — open times (C03)", () => {
  async function chooseArtist(user: ReturnType<typeof userEvent.setup>) {
    await user.click(screen.getByLabelText(/^artist/i));
    await user.click(await screen.findByRole("option", { name: "Luna Artista" }));
  }

  it("asks for an artist first, then picking an open time fills Date & Time on the studio's clock", async () => {
    mockSlots = weekWithTwoOpenTimes;
    const user = userEvent.setup();
    renderForm();
    expect(screen.getByText(/choose an artist, or let the studio choose, to see open times/i)).toBeInTheDocument();

    await chooseArtist(user);
    await user.click(await screen.findByRole("button", { name: "10:00" }));

    expect(screen.getByLabelText(/date.*time/i)).toHaveValue(`${openDay()}T10:00`);
    expect(screen.getByRole("button", { name: "10:00" })).toHaveAttribute("aria-pressed", "true");
  });

  it("a time taken before submitting keeps every entered value, offers the nearest open times, and submits nothing by itself", async () => {
    mockSlots = weekWithTwoOpenTimes;
    mockCreateGuestAppointment.mockReturnValueOnce(Promise.resolve({
      error: { status: 409, data: { code: "SLOT_ALREADY_BOOKED", message: "The selected time slot is no longer available." } },
    }));
    const user = userEvent.setup();
    renderForm();

    await fillIdentityFields(user);
    await chooseArtist(user);
    await user.click(await screen.findByRole("button", { name: "10:00" }));
    await enter(user, screen.getByLabelText(/what are you looking to get done/i), "A small rose");
    await uploadImages(user);
    await user.click(screen.getByRole("button", { name: /request appointment/i }));

    // Said plainly, with a fresh list asked for, and the chosen time NOT moved.
    expect(await screen.findByText(/was just booked by someone else/i)).toBeInTheDocument();
    expect(mockSlotsRefetch).toHaveBeenCalled();
    expect(screen.getByLabelText(/date.*time/i)).toHaveValue(`${openDay()}T10:00`);
    expect(screen.getByText("That time was just booked. Choose another time.")).toBeInTheDocument();
    // Every other value is kept.
    expect(screen.getByLabelText(/first name/i)).toHaveValue("Jamie");
    expect(screen.getByLabelText(/^email/i)).toHaveValue("jamie@example.com");
    expect(screen.getByLabelText(/what are you looking to get done/i)).toHaveValue("A small rose");
    expect(screen.getAllByRole("img")).toHaveLength(2);

    // Choosing the offered alternative only fills the field: nothing is sent until the person submits.
    const takenAlert = screen.getByText(/was just booked by someone else/i).closest("[role='alert']") as HTMLElement;
    const alternative = within(takenAlert).getByRole("button", { name: /13:00/ });
    await user.click(alternative);
    expect(screen.getByLabelText(/date.*time/i)).toHaveValue(`${openDay()}T13:00`);
    expect(mockCreateGuestAppointment).toHaveBeenCalledTimes(1);
    expect(screen.queryByText(/was just booked by someone else/i)).not.toBeInTheDocument();

    mockCreateGuestAppointment.mockReturnValueOnce(Promise.resolve({ data: { message: "ok" } }));
    await user.click(screen.getByRole("button", { name: /request appointment/i }));
    await screen.findByText("Check your email");
    expect(mockCreateGuestAppointment).toHaveBeenLastCalledWith(expect.objectContaining({
      body: expect.objectContaining({ booking: expect.objectContaining({ date: `${openDay()}T04:00:00.000Z` }) }),
    }));
  }, FULL_FORM_TIMEOUT);

  it("when open times can't load, it says so, offers Try again, and the time can still be typed", async () => {
    mockSlots = () => ({ data: undefined, isFetching: false, isError: true, refetch: mockSlotsRefetch });
    const user = userEvent.setup();
    renderForm();
    await chooseArtist(user);

    expect(screen.getByText(/couldn.t load open times\. you can still type a time below/i)).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /try again/i }));
    expect(mockSlotsRefetch).toHaveBeenCalled();

    await user.type(screen.getByLabelText(/date.*time/i), "2030-03-05T22:00");
    expect(screen.getByLabelText(/date.*time/i)).toHaveValue("2030-03-05T22:00");
  });
});
