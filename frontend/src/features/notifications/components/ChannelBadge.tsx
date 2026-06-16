import { Mail, MessageSquare } from "lucide-react";

export function ChannelBadge({ channel }: { channel: "Email" | "Sms" }) {
  if (channel === "Email") {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-blue-500/10 px-2 py-0.5 text-xs font-medium text-blue-600 dark:text-blue-400">
        <Mail className="h-3 w-3" />
        Email
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-purple-500/10 px-2 py-0.5 text-xs font-medium text-purple-600 dark:text-purple-400">
      <MessageSquare className="h-3 w-3" />
      SMS
    </span>
  );
}
