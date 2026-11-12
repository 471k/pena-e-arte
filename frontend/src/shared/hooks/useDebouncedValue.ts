import { useEffect, useState } from "react";

/**
 * `value`, once it has stopped changing for `delayMs`. For a search box that drives a server query: each
 * keystroke would otherwise be a request. A timer, not data fetching, so an effect is the right tool here.
 */
export function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);

  useEffect(() => {
    const timer = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(timer);
  }, [value, delayMs]);

  return debounced;
}
