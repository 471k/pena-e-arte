import { CheckCircle2, XCircle } from "lucide-react";
import { cn } from "@/shared/utils/cn";

interface PasswordMatchIndicatorProps {
  password: string;
  confirm: string;
}

export function PasswordMatchIndicator({ password, confirm }: PasswordMatchIndicatorProps) {
  if (!password || !confirm) return null;

  const matches = password === confirm;

  return (
    <p
      className={cn(
        "flex items-center gap-1.5 text-xs",
        matches ? "text-emerald-600 dark:text-emerald-400" : "text-destructive-text"
      )}
      aria-live="polite"
    >
      {matches ? <CheckCircle2 className="h-3.5 w-3.5" /> : <XCircle className="h-3.5 w-3.5" />}
      {matches ? "Passwords match" : "Doesn't match yet"}
    </p>
  );
}
