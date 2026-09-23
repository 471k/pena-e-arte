import { describe, it, expect } from "vitest";
import { yearlySavingLabel, yearlySavingPercentFloor } from "../utils/yearlySavingLabel";

describe("yearlySavingLabel", () => {
  it("79/790 and 29/290 both compute a whole 2 months free", () => {
    expect(yearlySavingLabel(79, 790)).toBe("2 months free");
    expect(yearlySavingLabel(29, 290)).toBe("2 months free");
  });

  it("59/590 computes a whole 2 months free (Growth)", () => {
    expect(yearlySavingLabel(59, 590)).toBe("2 months free");
  });

  it("singular '1 month free' when exactly one month is saved", () => {
    // 29 * 12 - 319 = 29 → 29/29 = 1 month
    expect(yearlySavingLabel(29, 319)).toBe("1 month free");
  });

  it("79/800 falls back to a floored percentage", () => {
    expect(yearlySavingLabel(79, 800)).toBe("save 15%");
  });

  it("79/1000 (yearly costs more than monthly) returns null", () => {
    expect(yearlySavingLabel(79, 1000)).toBeNull();
  });

  it("0/0 (Free) returns null", () => {
    expect(yearlySavingLabel(0, 0)).toBeNull();
  });

  it("no saving (yearly == monthly * 12) returns null, not '0 months free'", () => {
    expect(yearlySavingLabel(79, 948)).toBeNull();
  });
});

describe("yearlySavingPercentFloor", () => {
  it("floors rather than rounds, so the label never overstates the saving", () => {
    // (1 - 800/948) * 100 = 15.61...
    expect(yearlySavingPercentFloor(79, 800)).toBe(15);
  });

  it("returns null when monthly is 0", () => {
    expect(yearlySavingPercentFloor(0, 0)).toBeNull();
  });

  it("returns null when the saving isn't positive", () => {
    expect(yearlySavingPercentFloor(79, 1000)).toBeNull();
  });
});
