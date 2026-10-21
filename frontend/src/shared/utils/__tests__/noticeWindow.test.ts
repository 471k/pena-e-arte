import { describe, it, expect } from "vitest";
import { formatNoticeWindow } from "@/shared/utils/noticeWindow";

describe("formatNoticeWindow", () => {
  it("keeps windows up to 48 h in hours, like the platform default", () => {
    expect(formatNoticeWindow(1)).toBe("1 hour");
    expect(formatNoticeWindow(24)).toBe("24 hours");
    expect(formatNoticeWindow(48)).toBe("48 hours");
  });

  it("shows longer whole-day windows in days (G-11: a 30-day policy read '720 hours')", () => {
    expect(formatNoticeWindow(72)).toBe("3 days");
    expect(formatNoticeWindow(720)).toBe("30 days");
  });

  it("keeps a longer window that isn't whole days in hours", () => {
    expect(formatNoticeWindow(60)).toBe("60 hours");
  });
});
