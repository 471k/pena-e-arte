import { useEffect, useRef, useState } from "react";

export interface GeocodedLocation {
  lat: number;
  lng: number;
  city: string;
  country: string;
}

export type GeocodeStatus = "idle" | "loading" | "success" | "error";

interface UseAddressGeocodeOptions {
  minLength?: number;
  debounceMs?: number;
  enabled?: boolean;
}

/**
 * Debounced forward-geocoding via Nominatim (OpenStreetMap) — mirrors the existing pattern in
 * location-picker.tsx's reverseGeocode and DiscoverPage.tsx's reverseGeocode/
 * handleLocationSearch (same host, same unauthenticated usage, no new dependency, no new API
 * key). See docs/claude/architecture.md's "known gaps" entry for why all four client-side call
 * sites should eventually move behind a backend proxy — out of scope for this hook.
 *
 * Calls `onResolved` at most once per settled (debounced) address value. Drops any response
 * that resolves after the input has changed again, so a fast typer never has an earlier,
 * now-stale geocode overwrite what a later keystroke already triggered.
 */
export function useAddressGeocode(
  address: string,
  onResolved: (loc: GeocodedLocation) => void,
  { minLength = 5, debounceMs = 700, enabled = true }: UseAddressGeocodeOptions = {},
) {
  const [asyncStatus, setAsyncStatus] = useState<GeocodeStatus>("idle");
  const latestAddressRef = useRef(address);
  const trimmed = address.trim();
  const belowMinLength = trimmed.length < minLength;

  useEffect(() => {
    // Ref mutation belongs in an effect, not render — react-hooks/refs.
    latestAddressRef.current = address;
    if (!enabled || belowMinLength) return;

    const controller = new AbortController();
    const timer = setTimeout(async () => {
      setAsyncStatus("loading");
      try {
        const res = await fetch(
          `https://nominatim.openstreetmap.org/search?format=jsonv2&addressdetails=1&limit=1&q=${encodeURIComponent(trimmed)}`,
          { headers: { "Accept-Language": "en" }, signal: controller.signal },
        );
        const results = (await res.json()) as Array<{
          lat: string;
          lon: string;
          address?: {
            city?: string;
            town?: string;
            village?: string;
            municipality?: string;
            country?: string;
          };
        }>;

        // Stale-response guard: a newer debounced call is already queued/running if the
        // address changed while this one was in flight.
        if (latestAddressRef.current.trim() !== trimmed) return;

        if (results.length === 0) {
          setAsyncStatus("error");
          return;
        }

        const [first] = results;
        const a = first.address ?? {};
        onResolved({
          lat: parseFloat(first.lat),
          lng: parseFloat(first.lon),
          city: a.city ?? a.town ?? a.village ?? a.municipality ?? "",
          country: a.country ?? "",
        });
        setAsyncStatus("success");
      } catch (err) {
        if (err instanceof DOMException && err.name === "AbortError") return;
        setAsyncStatus("error");
      }
    }, debounceMs);

    return () => {
      clearTimeout(timer);
      controller.abort();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [address, enabled, minLength, debounceMs, belowMinLength]);

  return { status: belowMinLength ? "idle" : asyncStatus };
}
