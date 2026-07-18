import { useEffect } from "react";

/**
 * Calls `onEscape` when the Escape key is pressed.
 * Only attaches a listener when `enabled` is true.
 */
export function useEscapeKey(enabled: boolean, onEscape: () => void): void {
  useEffect(() => {
    if (!enabled) return;
    function handler(e: KeyboardEvent) {
      if (e.key === "Escape") onEscape();
    }
    document.addEventListener("keydown", handler);
    return () => document.removeEventListener("keydown", handler);
  }, [enabled, onEscape]);
}
