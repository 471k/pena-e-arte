import type { ClientAppointmentAftercare } from "../appointment.types";

// E04c phase C: the client's aftercare block says what each channel actually did, from the server's
// per-channel records. "Sent" means our provider accepted the message; nothing here can say it was
// delivered or read, and no string here may claim either (the copy test checks).

type When = (iso: string) => string;

/** The line above the steps: emailed, tried but failed, or no record (no claim at all). */
export function aftercareIntro(aftercare: ClientAppointmentAftercare, when: When): string {
  const email = aftercare.channels?.find((c) => c.channel === "Email");
  const at = when(email?.at ?? aftercare.sentAt);
  if (email?.sent) return `The studio sent these instructions to your email on ${at}.`;
  if (email) return `The studio tried to email you these instructions on ${at}, but the email couldn't be sent. Here they are.`;
  return `The studio's aftercare instructions, from ${at}.`;
}

/** The text-message line, or null when no text message was attempted. */
export function aftercareTextLine(aftercare: ClientAppointmentAftercare, when: When): string | null {
  const sms = aftercare.channels?.find((c) => c.channel === "Sms");
  if (!sms) return null;
  return sms.sent
    ? `A short version was also sent to you by text message on ${when(sms.at ?? aftercare.sentAt)}.`
    : "A short version by text message couldn't be sent.";
}
