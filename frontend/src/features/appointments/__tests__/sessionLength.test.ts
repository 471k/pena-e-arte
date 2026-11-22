import { describe, it, expect } from "vitest";
import { describeSessionLength, formatSessionLength } from "@/features/appointments/sessionLength";

describe("session length wording (C04; slice 7 phase C's review block reads the same helper)", () => {
  it.each([
    [30, "30 min"], [45, "45 min"], [60, "1 hour"], [90, "1 hour 30 min"], [120, "2 hours"], [150, "2 hours 30 min"], [480, "8 hours"],
  ])("%i minutes reads %s", (min, text) => {
    expect(formatSessionLength(min)).toBe(text);
  });

  it("names its basis: the service, or the client's own choice", () => {
    expect(describeSessionLength(120, "Large colour session")).toBe("2 hours, set by “Large colour session”");
    expect(describeSessionLength(90, null)).toBe("1 hour 30 min, the length you chose");
  });
});
