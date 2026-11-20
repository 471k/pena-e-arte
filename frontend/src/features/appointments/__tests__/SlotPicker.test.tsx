import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SlotPicker, type SlotListQuery } from "../components/SlotPicker";
import type { AvailableSlotsResponse } from "../appointment.types";

const zone = { status: "ready", timeZone: "Europe/Paris" } as const;
const today = { year: 2027, month: 6, day: 7 };

function emptyWeek(nextAvailable: string | null): AvailableSlotsResponse {
  const days = Array.from({ length: 7 }, (_, i) => ({ date: `2027-06-${String(7 + i).padStart(2, "0")}`, starts: [] as string[] }));
  return { from: "2027-06-07", to: "2027-06-13", durationMinutes: 60, stepMinutes: 60, days, nextAvailable };
}

const query = (data: AvailableSlotsResponse | undefined, extra: Partial<SlotListQuery> = {}): SlotListQuery =>
  ({ data, isFetching: false, isError: false, refetch: vi.fn(), ...extra });

describe("SlotPicker (C03)", () => {
  it("with nothing open this week it offers the next open time, and choosing it moves to that week", async () => {
    const onRangeStart = vi.fn();
    const user = userEvent.setup();
    render(
      <SlotPicker id="t" zone={zone} today={today} rangeStart={today} onRangeStart={onRangeStart}
        query={query(emptyWeek("2027-06-17T12:00:00.000Z"))} selectedIso={null} onPick={vi.fn()} />,
    );

    expect(screen.getByText("No open times on these dates.")).toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: /next open time: thu 17 jun, 14:00/i }));

    expect(onRangeStart).toHaveBeenCalledWith({ year: 2027, month: 6, day: 14 });
  });

  it("with nothing open in the next 60 days it says so and points to typing a time", () => {
    render(
      <SlotPicker id="t" zone={zone} today={today} rangeStart={today} onRangeStart={vi.fn()}
        query={query(emptyWeek(null))} selectedIso={null} onPick={vi.fn()} />,
    );
    expect(screen.getByText(/no open times in the next 60 days\. you can still type a time below/i)).toBeInTheDocument();
  });

  it("can't go back before today", () => {
    render(
      <SlotPicker id="t" zone={zone} today={today} rangeStart={today} onRangeStart={vi.fn()}
        query={query(emptyWeek(null))} selectedIso={null} onPick={vi.fn()} />,
    );
    expect(screen.getByRole("button", { name: /earlier dates/i })).toBeDisabled();
    expect(screen.getByRole("button", { name: /later dates/i })).toBeEnabled();
  });

  it("shows a reason instead of a list when it can't be asked yet", () => {
    render(
      <SlotPicker id="t" zone={zone} today={today} rangeStart={today} onRangeStart={vi.fn()}
        query={null} waitingFor="Choose an artist first." selectedIso={null} onPick={vi.fn()} />,
    );
    expect(screen.getByText("Choose an artist first.")).toBeInTheDocument();
  });
});
