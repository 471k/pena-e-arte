import { describe, it, expect } from "vitest";
import { claimHandoffDefaults, readClaimTokenFromHash, readWaitlistClaimHandoff } from "@/features/waitlist/waitlistClaim";

const VALID = [30, 45, 60, 90, 120] as const;

describe("readClaimTokenFromHash", () => {
  it("reads t from the fragment", () => {
    expect(readClaimTokenFromHash("#t=abc_DEF-123")).toBe("abc_DEF-123");
  });
  it("is null with no token", () => {
    expect(readClaimTokenFromHash("")).toBeNull();
    expect(readClaimTokenFromHash("#t=")).toBeNull();
    expect(readClaimTokenFromHash("#other=1")).toBeNull();
  });
});

describe("readWaitlistClaimHandoff", () => {
  it("accepts a well-formed handoff", () => {
    expect(readWaitlistClaimHandoff({
      waitlistClaim: { token: "t", artistId: "a", slotStart: "2030-01-01T10:00:00Z", durationMinutes: 90 },
    })).toEqual({ token: "t", artistId: "a", slotStart: "2030-01-01T10:00:00Z", durationMinutes: 90 });
  });
  it("ignores other router state (e.g. the flash catalog's) and junk", () => {
    expect(readWaitlistClaimHandoff(null)).toBeNull();
    expect(readWaitlistClaimHandoff({ flashDesignId: "d" })).toBeNull();
    expect(readWaitlistClaimHandoff({ waitlistClaim: { token: "" } })).toBeNull();
  });
});

describe("claimHandoffDefaults", () => {
  it("prefills artist, start (as the instant the form will submit back) and a preset duration", () => {
    const slot = "2030-01-01T10:30:00Z";
    const d = claimHandoffDefaults({ token: "t", artistId: "a", slotStart: slot, durationMinutes: 90 }, VALID);

    expect(d.artistId).toBe("a");
    expect(d.bookAnyArtist).toBe(false);
    expect(d.durationMinutes).toBe(90);
    // The forms submit new Date(value).toISOString(): the round trip must give the offered instant back.
    expect(new Date(d.scheduledAt!).toISOString()).toBe(new Date(slot).toISOString());
  });
  it("leaves a non-preset duration for the client to pick", () => {
    const d = claimHandoffDefaults({ token: "t", artistId: "a", slotStart: null, durationMinutes: 75 }, VALID);
    expect(d.durationMinutes).toBeUndefined();
    expect(d.scheduledAt).toBeUndefined();
  });
  it("a freed slot with no artist books with any artist", () => {
    const d = claimHandoffDefaults({ token: "t", artistId: null, slotStart: null, durationMinutes: 60 }, VALID);
    expect(d.bookAnyArtist).toBe(true);
  });
  it("nothing without a handoff", () => {
    expect(claimHandoffDefaults(null, VALID)).toEqual({});
  });
});
