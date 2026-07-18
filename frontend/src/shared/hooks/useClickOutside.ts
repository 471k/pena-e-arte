import { type RefObject, useEffect } from "react";

/**
 * Calls `onClickOutside` when a mousedown event fires outside `ref`.
 * Only active when `enabled` is true — pass the open/closed state of the
 * dropdown to avoid attaching a global listener when the panel is closed.
 */
export function useClickOutside<T extends HTMLElement>(
  ref:            RefObject<T | null>,
  enabled:        boolean,
  onClickOutside: () => void,
): void {
  useEffect(() => {
    if (!enabled) return;
    function handler(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        onClickOutside();
      }
    }
    document.addEventListener("mousedown", handler);
    return () => document.removeEventListener("mousedown", handler);
  }, [ref, enabled, onClickOutside]);
}
