import { useId, useState } from "react";
import { cn } from "@/shared/utils/cn";

/** Text longer than this collapses behind "Show more". */
export const SHOW_MORE_CHARS = 240;

function cutAtWord(text: string, max: number): string {
  if (text.length <= max) return text;
  const cut = text.slice(0, max);
  return `${cut.slice(0, Math.max(cut.lastIndexOf(" "), max - 30)).trimEnd()}…`;
}

interface ShowMoreTextProps {
  text: string;
  className?: string;
  /** Collapse threshold in characters; defaults to SHOW_MORE_CHARS. */
  maxChars?: number;
}

/**
 * Long free text (revision notes, client feedback) shown in full only on request (E04c §5.2). The
 * toggle is a real button with aria-expanded pointing at the text it controls, so it works from the
 * keyboard and a screen reader announces the state. Short text renders as-is with no toggle.
 */
export function ShowMoreText({ text, className, maxChars = SHOW_MORE_CHARS }: ShowMoreTextProps) {
  const [expanded, setExpanded] = useState(false);
  const textId = useId();
  const cls = cn("whitespace-pre-wrap break-words", className);
  if (text.length <= maxChars) return <p className={cls}>{text}</p>;
  return (
    <div className="space-y-1">
      <p id={textId} className={cls}>{expanded ? text : cutAtWord(text, maxChars)}</p>
      <button
        type="button"
        className="text-xs text-link underline underline-offset-2"
        aria-expanded={expanded}
        aria-controls={textId}
        onClick={() => setExpanded((v) => !v)}
      >
        {expanded ? "Show less" : "Show more"}
      </button>
    </div>
  );
}
