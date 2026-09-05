import "leaflet/dist/leaflet.css";
import { divIcon } from "leaflet";
import { MapContainer, Marker, Popup, TileLayer } from "react-leaflet";
import { Link } from "react-router-dom";
import { MapPin, PenLine } from "lucide-react";
import { useGetStudioMapQuery } from "@/features/studios";
import { buildGoogleMapsDirectionsUrl, hasPinnedLocation } from "@/shared/utils/googleMaps";

const studioPin = divIcon({
  className: "",
  html: `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 36" width="24" height="36">
    <path d="M12 0C5.373 0 0 5.373 0 12c0 9 12 24 12 24S24 21 24 12C24 5.373 18.627 0 12 0z" fill="#0f172a"/>
    <circle cx="12" cy="12" r="4.5" fill="white"/>
  </svg>`,
  iconSize: [24, 36],
  iconAnchor: [12, 36],
  popupAnchor: [0, -38],
});

const DEFAULT_CENTER: [number, number] = [38.7169, -9.1395];
const DEFAULT_ZOOM = 6;

export function StudioMapPage() {
  const { data: studios, isLoading, isError } = useGetStudioMapQuery();

  return (
    <div className="flex flex-col h-screen">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background shrink-0 z-[1000]">
        <div className="flex items-center gap-2">
          <PenLine className="h-5 w-5" />
          <span className="font-semibold tracking-tight">TattooOS</span>
        </div>
        <nav className="flex items-center gap-3">
          <Link
            to="/discover"
            className="text-sm text-muted-foreground hover:text-foreground transition-colors"
          >
            List view
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

      <div className="relative flex-1 overflow-hidden">
        <MapContainer
          center={DEFAULT_CENTER}
          zoom={DEFAULT_ZOOM}
          className="h-full w-full"
          zoomControl
        >
          <TileLayer
            attribution='&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors'
            url="https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png"
          />
          {studios?.map((studio) => (
            <Marker
              key={studio.id}
              position={[studio.latitude, studio.longitude]}
              icon={studioPin}
            >
              <Popup>
                <div className="min-w-[160px] space-y-2 py-0.5">
                  <p className="font-semibold text-sm leading-tight">{studio.name}</p>
                  <p className="flex items-center gap-1 text-xs text-muted-foreground">
                    <MapPin className="h-3 w-3 shrink-0" />
                    {studio.city}
                  </p>
                  <a
                    href={`/s/${studio.slug}`}
                    className="block text-xs font-medium text-primary hover:underline"
                  >
                    View studio →
                  </a>
                  {hasPinnedLocation(studio.latitude, studio.longitude) && (
                    <a
                      href={buildGoogleMapsDirectionsUrl(studio.latitude, studio.longitude)}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="block text-xs font-medium text-primary hover:underline"
                    >
                      Get directions →
                    </a>
                  )}
                </div>
              </Popup>
            </Marker>
          ))}
        </MapContainer>

        {isLoading && (
          <div className="absolute bottom-6 left-1/2 -translate-x-1/2 z-[1000] bg-background border rounded-full px-4 py-1.5 text-xs text-muted-foreground shadow-md">
            Loading studios…
          </div>
        )}

        {isError && (
          <div className="absolute bottom-6 left-1/2 -translate-x-1/2 z-[1000] bg-background border border-destructive rounded-full px-4 py-1.5 text-xs text-destructive-text shadow-md">
            Failed to load studios.
          </div>
        )}

        {!isLoading && !isError && studios?.length === 0 && (
          <div className="absolute bottom-6 left-1/2 -translate-x-1/2 z-[1000] bg-background border rounded-full px-4 py-1.5 text-xs text-muted-foreground shadow-md">
            No studios on the map yet.
          </div>
        )}
      </div>
    </div>
  );
}
