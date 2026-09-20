// Shared "is the owner currently viewing their own dual-role artist context" detector, used by
// both ArtistModeSwitcher (active-tab styling) and OwnerLayout (which nav item set to render).
// Two signals count as "artist context": the owner's own artist-profile page itself (and
// /earnings, which is always personal), or any shared page reached via an artist-mode nav link
// carrying `?artistId=<their own artist id>` (Schedule, Designs, Conduct Reports — pages whose
// underlying query already supports filtering to a specific artist for a non-artist-role
// caller, see GetAppointmentsQuery/GetDesignsQuery/GetMyConductReportsAsArtistQuery).
export function isInArtistContext(pathname: string, search: string, myArtistId: string | undefined): boolean {
  if (!myArtistId) return false;
  if (pathname.startsWith(`/artists/${myArtistId}`)) return true;
  if (pathname === "/earnings") return true;
  return new URLSearchParams(search).get("artistId") === myArtistId;
}
