/**
 * Builds a Google Maps "get directions" deep link for a pinned lat/lng, using
 * Google's documented, key-free Maps URL API:
 * https://developers.google.com/maps/documentation/urls/get-started#directions-action
 *
 * The same URL works everywhere: it opens the native Google Maps app via universal
 * link on iOS/Android when installed, and falls back to Google Maps on the web.
 */
export function buildGoogleMapsDirectionsUrl(latitude: number, longitude: number): string {
  const params = new URLSearchParams({
    api: "1",
    destination: `${latitude},${longitude}`,
  });
  return `https://www.google.com/maps/dir/?${params.toString()}`;
}

/**
 * A studio's location is only meaningfully "pinned" once it's away from (0, 0) —
 * same sentinel convention `LocationPicker`'s `hasInitial` check already uses
 * (`shared/components/ui/location-picker.tsx`). `RegisterStudioValidator` only
 * range-checks latitude/longitude ([-90,90]/[-180,180]); it doesn't reject the
 * origin outright, so this guard is a real, load-bearing check, not defensive
 * paranoia — a studio whose location was never actually set should not render a
 * "Get Directions" link that points at Null Island.
 */
export function hasPinnedLocation(latitude: number, longitude: number): boolean {
  return !Number.isNaN(latitude) && !Number.isNaN(longitude) && !(latitude === 0 && longitude === 0);
}
