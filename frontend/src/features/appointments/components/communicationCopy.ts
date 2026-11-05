import type { AftercareCommunication, CommunicationChannel } from "../appointment.types";

// The words for communication state (E04b, A08). They say only what the records prove: a message was
// not sent, was sent (our side handed it to the provider, or posted it in the conversation), or failed.
// They never say or imply that it reached the client or that the client looked at it, because nothing
// records that for email or text. A test fails if a forbidden word appears here.

export type FormatWhen = (iso: string) => string;

export const CHANNEL_LABEL: Record<CommunicationChannel["channel"], string> = {
  Email: "Email",
  Sms: "Text message",
};

export function rescheduleNotSent(movedAt: string, when: FormatWhen): string {
  return `Moved on ${when(movedAt)}. The client isn't told automatically, and nobody has messaged them about it yet.`;
}

export function rescheduleSent(movedAt: string, messagedAt: string, when: FormatWhen): string {
  return `Moved on ${when(movedAt)}. A message about it was sent to the client on ${when(messagedAt)}.`;
}

export const NO_ACCOUNT = "The client has no TattooOS account, so let them know another way.";

export function aftercareSummary(a: AftercareCommunication, when: FormatWhen): string {
  switch (a.state) {
    case "NotSent":
      return "Aftercare hasn't been sent.";
    case "Sent":
      return "Aftercare was sent.";
    case "Failed":
      return "Aftercare couldn't be sent: every attempt failed. Send the client the instructions another way.";
    case "NotRecorded":
    default:
      return a.ranAt
        ? `Aftercare ran on ${when(a.ranAt)}, but there's no record of a message going out. It may be older than this record, or aftercare messages may be switched off.`
        : "Aftercare hasn't been sent.";
  }
}

export function channelLine(c: CommunicationChannel, when: FormatWhen): string {
  const at = c.at ? ` on ${when(c.at)}` : "";
  return c.sent ? `${CHANNEL_LABEL[c.channel]}: sent${at}` : `${CHANNEL_LABEL[c.channel]}: failed to send${at}`;
}
