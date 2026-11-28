import "leaflet/dist/leaflet.css";
import { divIcon } from "leaflet";
import { MapContainer, Marker, Popup, TileLayer } from "react-leaflet";
import { Link } from "react-router-dom";
import { MapPin } from "lucide-react";
import { useGetStudioMapQuery } from "@/features/studios";
import { buildGoogleMapsDirectionsUrl, hasPinnedLocation } from "@/shared/utils/googleMaps";
import { TattooOSLogo } from "@/shared/components/TattooOSLogo";

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
      {/* At 200% text on a phone the nav cannot sit beside the brand: the header wraps it onto its own row and
          the nav wraps its links, instead of "Register your studio" running off-screen. Padding and gaps are px
          so they do not double with the text. At 100% text the row is unchanged. */}
      <header className="flex flex-wrap items-center justify-between gap-x-[12px] gap-y-2 px-[24px] py-3 border-b bg-background shrink-0 z-[1000]">
        {/* Brand master 1.0.0, the same lockup and swap as the other public headers (BrandMark in
            PublicPageHeader). Not a link: this header's brand never was one, and its links stay as
            they are. The small lockup needs a 323px window here (measured 28 Nov 2026, Chrome, at
            100% text), so it keeps the package's own sub-360px rule: below 360px, the 20px micro
            mark. Only one image is displayed at a time, so the page names "TattooOS" once, as the
            old wordmark text did. The visibility classes sit on wrappers because TattooOSLogo.css
            sets the logo's own display. */}
        <div className="flex items-center">
          <span className="inline-flex max-[359px]:hidden">
            <TattooOSLogo variant="small" />
          </span>
          <span className="hidden max-[359px]:inline-flex">
            <TattooOSLogo variant="micro" height={20} />
          </span>
        </div>
        <nav className="flex flex-wrap items-center justify-end gap-x-[12px] gap-y-2">
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
            className="text-sm font-medium text-center bg-foreground text-background px-3 py-1.5 rounded-md hover:opacity-90 transition-opacity"
          >
            Register your studio
          </Link>
        </nav>
      </header>

      <div className="relative isolate flex-1 overflow-hidden">
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
                    className="block text-xs font-medium text-link hover:underline"
                  >
                    View studio →
                  </a>
                  {hasPinnedLocation(studio.latitude, studio.longitude) && (
                    <a
                      href={buildGoogleMapsDirectionsUrl(studio.latitude, studio.longitude)}
                      target="_blank"
                      rel="noopener noreferrer"
                      className="block text-xs font-medium text-link hover:underline"
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
