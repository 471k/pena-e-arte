import { describe, expect, it } from "vitest";
import { isSlotTaken, nearestStarts, rangeStartContaining, slotRange } from "../slotList";
import type { AvailableSlotsResponse } from "../appointment.types";

const data = (starts: string[]): AvailableSlotsResponse => ({
  from: "2027-06-07", to: "2027-06-13", durationMinutes: 60, stepMinutes: 60,
  days: [{ date: "2027-06-07", starts }], nextAvailable: starts[0] ?? null,
});

describe("slotList (C03)", () => {
  it("a range is seven studio days as the API's dates", () => {
    expect(slotRange({ year: 2027, month: 6, day: 28 })).toEqual({ from: "2027-06-28", to: "2027-07-04" });
  });

  it("the nearest open times to a taken one leave the taken time out and come back in time order", () => {
    const taken = "2027-06-07T10:00:00.000Z";
    const result = nearestStarts(data([
      "2027-06-07T08:00:00.000Z", "2027-06-07T09:00:00.000Z", taken,
      "2027-06-07T11:00:00.000Z", "2027-06-07T15:00:00.000Z",
    ]), taken);

    expect(result).toEqual(["2027-06-07T09:00:00.000Z", "2027-06-07T11:00:00.000Z", "2027-06-07T08:00:00.000Z"].sort());
  });

  it("no list means no alternatives, never a guess", () => {
    expect(nearestStarts(undefined, "2027-06-07T10:00:00.000Z")).toEqual([]);
  });

  it("only a 409 with SLOT_ALREADY_BOOKED counts as a taken slot", () => {
    expect(isSlotTaken({ status: 409, data: { code: "SLOT_ALREADY_BOOKED" } })).toBe(true);
    expect(isSlotTaken({ status: 409, data: { code: "CANCELLATION_QUOTE_CHANGED" } })).toBe(false);
    expect(isSlotTaken({ status: 409, data: { message: "The selected time slot is no longer available." } })).toBe(false);
    expect(isSlotTaken({ status: 422, data: { code: "SLOT_ALREADY_BOOKED" } })).toBe(false);
    expect(isSlotTaken(undefined)).toBe(false);
  });

  it("jumping to a later time lands on the seven-day window that holds it, counted from today, on the studio's calendar", () => {
    const today = { year: 2027, month: 6, day: 7 };
    // 22:30 UTC on 16 June is already 17 June in Tokyo: the 14-20 June window is the right one.
    expect(rangeStartContaining(new Date("2027-06-16T22:30:00Z"), today, "Asia/Tokyo")).toEqual({ year: 2027, month: 6, day: 14 });
    expect(rangeStartContaining(new Date("2027-06-08T01:00:00Z"), today, "Asia/Tokyo")).toEqual(today);
  });
});
