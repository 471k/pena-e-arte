import { useState, useCallback } from "react";

export function useCopyToClipboard(timeoutMs = 1500): [boolean, (text: string) => void] {
  const [copied, setCopied] = useState(false);

  const copy = useCallback((text: string) => {
    navigator.clipboard.writeText(text).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), timeoutMs);
    });
  }, [timeoutMs]);

  return [copied, copy];
}
