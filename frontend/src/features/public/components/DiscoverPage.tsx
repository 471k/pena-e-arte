import { useEffect, useRef, useState } from "react";
import { Link } from "react-router-dom";
import { MapPin, PenLine, Search, Users } from "lucide-react";
import { Button }   from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Card, CardContent } from "@/shared/components/ui/card";
import { useGetNearbyStudiosQuery, type NearbyStudioResponse } from "../publicApi";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";

const RADII = [10, 25, 50, 100] as const;
type Radius = (typeof RADII)[number];

const DEFAULT_LAT = 38.7169;
const DEFAULT_LNG = -9.1395;

interface NominatimResult {
  lat:          string;
  lon:          string;
  display_name: string;
}

function DiscoverMeta() {
  useDocumentMeta({
    title:       "Find Tattoo Studios Near You — Pena e Artë",
    description: "Browse tattoo studios and artists near your location.",
    canonical:   "https://penaearte.com/discover",
  });
  return null;
}

function StudioCard({ studio }: { studio: NearbyStudioResponse }) {
  return (
    <Link to={`/s/${studio.slug}`}>
      <Card className="hover:border-ring transition-colors cursor-pointer h-full">
        {studio.coverImageUrl ? (
          <div className="h-32 bg-muted overflow-hidden rounded-t-lg">
            <img
              src={studio.coverImageUrl}
              alt={studio.name}
              className="w-full h-full object-cover"
              loading="lazy"
            />
          </div>
        ) : (
          <div className="h-32 bg-muted rounded-t-lg flex items-center justify-center">
            <PenLine className="h-8 w-8 text-muted-foreground/30" />
          </div>
        )}
        <CardContent className="p-4 space-y-1.5">
          <p className="font-medium text-sm leading-tight">{studio.name}</p>
          <div className="flex items-center gap-1 text-xs text-muted-foreground">
            <MapPin className="h-3 w-3 shrink-0" />
            <span>{studio.city}</span>
            <span className="ml-auto text-primary font-medium">
              {studio.distanceKm < 1
                ? `${Math.round(studio.distanceKm * 1000)} m`
                : `${studio.distanceKm} km`}
            </span>
          </div>
          {studio.artistCount > 0 && (
            <div className="flex items-center gap-1 text-xs text-muted-foreground">
              <Users className="h-3 w-3 shrink-0" />
              <span>{studio.artistCount} artist{studio.artistCount !== 1 ? "s" : ""}</span>
            </div>
          )}
        </CardContent>
      </Card>
    </Link>
  );
}

function DiscoverSkeleton() {
  return (
    <div
      className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3"
      aria-label="Loading studios"
    >
      {Array.from({ length: 6 }).map((_, i) => (
        <div key={i} className="space-y-0">
          <Skeleton className="h-32 w-full rounded-t-lg" />
          <div className="border border-t-0 rounded-b-lg p-4 space-y-2">
            <Skeleton className="h-4 w-32" />
            <Skeleton className="h-3 w-24" />
          </div>
        </div>
      ))}
    </div>
  );
}

export function DiscoverPage() {
  const hasGeo = "geolocation" in navigator;
  const [lat,          setLat]          = useState<number | null>(hasGeo ? null : DEFAULT_LAT);
  const [lng,          setLng]          = useState<number | null>(hasGeo ? null : DEFAULT_LNG);
  const [locationName, setLocationName] = useState<string>(hasGeo ? "Detecting location…" : "Lisbon, Portugal");
  const [radiusKm,     setRadiusKm]     = useState<Radius>(50);
  const [searchInput,  setSearchInput]  = useState<string>("");
  const [searchError,  setSearchError]  = useState<string | null>(null);
  const [isGeocoding,  setIsGeocoding]  = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!hasGeo) return;

    navigator.geolocation.getCurrentPosition(
      (pos) => {
        setLat(pos.coords.latitude);
        setLng(pos.coords.longitude);
        setLocationName("Your location");
      },
      () => {
        setLat(DEFAULT_LAT);
        setLng(DEFAULT_LNG);
        setLocationName("Lisbon, Portugal");
      },
      { timeout: 8000, maximumAge: 60_000 },
    );
  }, [hasGeo]);

  const { data: studios, isLoading: isStudiosLoading, isFetching } =
    useGetNearbyStudiosQuery(
      { lat: lat!, lng: lng!, radiusKm },
      { skip: lat === null || lng === null },
    );

  async function handleLocationSearch() {
    const q = searchInput.trim();
    if (!q) return;

    setIsGeocoding(true);
    setSearchError(null);

    try {
      const res = await fetch(
        `https://nominatim.openstreetmap.org/search?format=json&q=${encodeURIComponent(q)}&limit=1`,
        { headers: { "Accept-Language": "en" } },
      );
      const results: NominatimResult[] = await res.json() as NominatimResult[];

      if (results.length === 0) {
        setSearchError("Location not found. Try a different city name.");
        return;
      }

      const [first] = results;
      setLat(parseFloat(first.lat));
      setLng(parseFloat(first.lon));
      setLocationName(first.display_name.split(",").slice(0, 2).join(", "));
      setSearchInput("");
    } catch {
      setSearchError("Could not reach location service. Try again.");
    } finally {
      setIsGeocoding(false);
    }
  }

  const isLoading = lat === null || isStudiosLoading || isFetching;

  return (
    <div className="min-h-screen bg-background">
      <DiscoverMeta />

      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-[100]">
        <div className="flex items-center gap-2">
          <PenLine className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Pena e Artë</span>
        </div>
        <nav className="flex items-center gap-3">
          <Link
            to="/map"
            className="text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            Map view
          </Link>
          <Link
            to="/login"
            className="text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            Sign in
          </Link>
          <Link
            to="/register"
            className="text-sm font-medium bg-foreground text-background px-3 py-1.5 rounded-md hover:opacity-90 transition-opacity"
          >
            Register your studio
          </Link>
        </nav>
      </header>

      <main className="max-w-5xl mx-auto px-4 py-8 space-y-6">
        <div className="space-y-1">
          <h1 className="text-2xl font-bold tracking-tight">Find tattoo studios near you</h1>
          {lat !== null && (
            <p className="text-sm text-muted-foreground flex items-center gap-1">
              <MapPin className="h-3.5 w-3.5" />
              {locationName}
            </p>
          )}
        </div>

        <div className="flex flex-col gap-3 sm:flex-row sm:items-end">
          <div className="flex flex-1 gap-2">
            <input
              ref={inputRef}
              type="text"
              placeholder="Search a city or address…"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              onKeyDown={(e) => { if (e.key === "Enter") void handleLocationSearch(); }}
              className="flex-1 h-9 rounded-md border bg-background px-3 text-sm
                         focus:outline-none focus:ring-1 focus:ring-ring
                         placeholder:text-muted-foreground"
              aria-label="Search location"
            />
            <Button
              size="sm"
              variant="outline"
              onClick={() => void handleLocationSearch()}
              disabled={isGeocoding || !searchInput.trim()}
              aria-label="Search"
            >
              <Search className="h-4 w-4" />
            </Button>
          </div>

          <div className="flex items-center gap-2">
            <label htmlFor="radius-select" className="text-xs text-muted-foreground whitespace-nowrap">
              Within
            </label>
            <select
              id="radius-select"
              value={radiusKm}
              onChange={(e) => setRadiusKm(parseInt(e.target.value, 10) as Radius)}
              className="h-9 rounded-md border bg-background px-2 text-sm text-foreground
                         focus:outline-none focus:ring-1 focus:ring-ring"
            >
              {RADII.map((r) => (
                <option key={r} value={r}>{r} km</option>
              ))}
            </select>
          </div>
        </div>

        {searchError && (
          <p className="text-xs text-destructive" role="alert">{searchError}</p>
        )}

        {isLoading ? (
          <DiscoverSkeleton />
        ) : !studios || studios.length === 0 ? (
          <div className="flex flex-col items-center gap-3 py-16 text-center">
            <MapPin className="h-9 w-9 text-muted-foreground/40" />
            <p className="text-sm font-medium">No studios found nearby</p>
            <p className="text-xs text-muted-foreground">
              Try increasing the search radius or searching a different location.
            </p>
          </div>
        ) : (
          <>
            <p className="text-xs text-muted-foreground">
              {studios.length} studio{studios.length !== 1 ? "s" : ""} within {radiusKm} km
            </p>
            <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
              {studios.map((s) => (
                <StudioCard key={s.studioId} studio={s} />
              ))}
            </div>
          </>
        )}
      </main>

      <footer className="py-3 text-center text-xs text-muted-foreground border-t mt-8">
        <a
          href="https://penaearte.com"
          target="_blank"
          rel="noopener noreferrer"
          className="hover:underline"
        >
          Powered by Pena e Artë
        </a>
      </footer>
    </div>
  );
}
