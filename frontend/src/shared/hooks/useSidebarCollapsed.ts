import { useCallback, useSyncExternalStore } from "react";

const COLLAPSED_KEY = "sidebar-collapsed";

const listeners = new Set<() => void>();

// Only consulted when localStorage itself is unavailable (private mode, blocked site data), so the
// toggle still works for the current page view even though the preference can't persist.
let memoryFallback = false;

function read(): boolean {
  try {
    return localStorage.getItem(COLLAPSED_KEY) === "1";
  } catch {
    return memoryFallback;
  }
}

function write(value: boolean) {
  memoryFallback = value;
  try {
    localStorage.setItem(COLLAPSED_KEY, value ? "1" : "0");
  } catch {
    // storage unavailable — the preference just won't persist
  }
  listeners.forEach((notify) => notify());
}

function subscribe(notify: () => void): () => void {
  listeners.add(notify);
  // Keeps two open tabs in step, and picks up a preference changed by another tab.
  window.addEventListener("storage", notify);
  return () => {
    listeners.delete(notify);
    window.removeEventListener("storage", notify);
  };
}

/**
 * The desktop sidebar's collapsed/expanded preference, shared between the sidebar itself and the
 * toggle button that lives in the layout header (so the control stays on screen however tall the
 * banners above the header are). Persisted in localStorage.
 */
export function useSidebarCollapsed() {
  const collapsed = useSyncExternalStore(subscribe, read, () => false);
  const setCollapsed = useCallback((value: boolean) => write(value), []);
  const toggle = useCallback(() => write(!read()), []);
  return { collapsed, setCollapsed, toggle };
}
