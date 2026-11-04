import { describe, it, expect } from "vitest";
import { Role } from "@/shared/types/roles";
import {
  appointmentHrefForViewer,
  formatAppointmentLabel,
  showsAppointmentLabel,
} from "../appointmentLabel";
import type { ChatMessageResponse, MessageAppointmentLabel } from "../messaging.types";

const NOW = new Date("2026-11-04T12:00:00Z");

function label(overrides: Partial<MessageAppointmentLabel> = {}): MessageAppointmentLabel {
  return { appointmentId: "appt-1", startsAt: "2026-12-03T13:30:00Z", serviceName: "Fine line", ...overrides };
}

function message(id: string, appointment: MessageAppointmentLabel | null): ChatMessageResponse {
  return {
    id, conversationId: "c1", senderUserId: "u1", senderRole: "client", body: id,
    createdAt: "2026-11-01T10:00:00Z", readAt: null,
    appointmentId: appointment?.appointmentId ?? null, appointment,
  };
}

describe("formatAppointmentLabel", () => {
  it("shows the studio-local day and time with the service", () => {
    // 13:30 UTC is 14:30 in Tirana in December (UTC+1), whatever zone the test runner is in.
    expect(formatAppointmentLabel(label(), "Europe/Tirane", NOW)).toBe("Thu 3 Dec, 14:30 · Fine line");
  });

  it("uses the studio's timezone, not the browser's", () => {
    expect(formatAppointmentLabel(label(), "America/New_York", NOW)).toBe("Thu 3 Dec, 08:30 · Fine line");
  });

  it("falls back to the date and time alone when there is no service", () => {
    const text = formatAppointmentLabel(label({ serviceName: null }), "Europe/Tirane", NOW);

    expect(text).toBe("Thu 3 Dec, 14:30");
    expect(text).not.toMatch(/·|null/);
  });

  it("adds the year only when it isn't the current one", () => {
    expect(formatAppointmentLabel(label({ startsAt: "2027-01-12T09:00:00Z" }), "Europe/Tirane", NOW))
      .toBe("Tue, 12 Jan 2027, 10:00 · Fine line");
  });
});

describe("appointmentHrefForViewer", () => {
  it("sends a client to their own appointment page", () => {
    expect(appointmentHrefForViewer(Role.Client, "appt-1")).toBe("/my-appointments/appt-1");
  });

  it.each([Role.Artist, Role.Owner, Role.Admin])("sends %s to the staff appointment page", (role) => {
    expect(appointmentHrefForViewer(role, "appt-1")).toBe("/appointments/appt-1");
  });

  it("never gives a client the staff route", () => {
    expect(appointmentHrefForViewer(Role.Client, "appt-1")).not.toMatch(/^\/appointments\//);
  });

  it("gives no link at all when the role is unknown", () => {
    expect(appointmentHrefForViewer(null, "appt-1")).toBeNull();
    expect(appointmentHrefForViewer(undefined, "appt-1")).toBeNull();
  });
});

describe("showsAppointmentLabel", () => {
  it("shows the label once per run about the same appointment, and again where it changes", () => {
    const a = label({ appointmentId: "a" });
    const b = label({ appointmentId: "b" });
    const thread = [
      message("1", a), message("2", a), message("3", null), message("4", a), message("5", b), message("6", b),
    ];

    const shown = thread.filter((m, i) => showsAppointmentLabel(m, thread[i - 1])).map((m) => m.id);

    expect(shown).toEqual(["1", "4", "5"]);
  });
});
