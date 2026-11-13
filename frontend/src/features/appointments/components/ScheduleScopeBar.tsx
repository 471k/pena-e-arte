import { useId } from "react";
import { Link } from "react-router-dom";
import { Users } from "lucide-react";
import { useGetArtistsQuery, type ArtistResponse } from "@/features/artists/artistsApi";
import type { ScheduleScope } from "../scheduleScope";

/**
 * Says whose appointments the schedule is showing, every time (E05c, O03), and gives an owner or admin
 * the artist filter that until now existed only as a URL parameter. The sentence is the statement of
 * scope; the dropdown is the control. Neither is left to be inferred from the other's position.
 *
 * Requests with no artist are not counted here. The owner's dashboard ("Needs you", E05a) already counts
 * them from the server; this links there rather than computing the same number a second way.
 */

interface ScheduleScopeBarProps {
  scope: ScheduleScope;
  /** An owner in their own artist view: the "own" sentence says it's their artist view. */
  isOwnerInArtistMode: boolean;
  /** Owner or admin outside artist mode. */
  canFilter: boolean;
  /** Owner only: the dashboard's "Needs you" is an owner page. */
  linkUnassignedToDashboard: boolean;
  onFilterChange: (artistId: string | null) => void;
}

function fullName(a: ArtistResponse): string {
  return `${a.firstName} ${a.lastName}`.trim();
}

const STUDIO_VALUE = "";

export function ScheduleScopeBar({
  scope, isOwnerInArtistMode, canFilter, linkUnassignedToDashboard, onFilterChange,
}: ScheduleScopeBarProps) {
  const selectId = useId();
  const { data: artists } = useGetArtistsQuery(undefined, { skip: !canFilter });

  const sorted = [...(artists ?? [])].sort((a, b) => fullName(a).localeCompare(fullName(b)));
  const selectedId = scope.kind === "artist" ? scope.artistId : STUDIO_VALUE;
  const selected = scope.kind === "artist" ? sorted.find((a) => a.id === scope.artistId) : undefined;
  // A filtered artist missing from the list (deactivated, or not loaded yet) still needs an option, or the
  // dropdown would silently show "All artists" while the list below is one artist's.
  const selectedMissing = scope.kind === "artist" && !selected;
  const selectedName = selected ? fullName(selected) : "one artist";

  return (
    <div className="border-b bg-muted/30 px-4 sm:px-6 py-2.5" data-testid="schedule-scope-bar">
      <div className="max-w-3xl mx-auto flex flex-wrap items-center gap-x-4 gap-y-2">
        {canFilter && (
          <div className="flex items-center gap-2 min-w-0">
            <label htmlFor={selectId} className="text-xs text-muted-foreground shrink-0">Artist</label>
            <select
              id={selectId}
              value={selectedId}
              onChange={(e) => onFilterChange(e.target.value === STUDIO_VALUE ? null : e.target.value)}
              className="h-8 max-w-[14rem] rounded-md border bg-background px-2 text-sm text-foreground focus:outline-none focus:ring-1 focus:ring-ring"
            >
              <option value={STUDIO_VALUE}>All artists (whole studio)</option>
              {selectedMissing && <option value={selectedId}>Selected artist</option>}
              {sorted.map((a) => (
                <option key={a.id} value={a.id}>{fullName(a)}</option>
              ))}
            </select>
          </div>
        )}

        <p role="status" data-testid="schedule-scope" className="flex items-start gap-1.5 text-xs text-muted-foreground min-w-0">
          <Users className="h-3.5 w-3.5 shrink-0 mt-px" aria-hidden="true" />
          <span className="min-w-0 break-words">
            {scope.kind === "own" && (
              <>
                Showing <strong className="font-medium text-foreground">your own appointments</strong>
                {isOwnerInArtistMode ? " (your artist view). Switch to Owner to see the whole studio." : "."}
              </>
            )}
            {scope.kind === "studio" && (
              <>
                Showing <strong className="font-medium text-foreground">every artist's appointments</strong>
                {" "}and requests with no artist yet, which read &ldquo;Unassigned&rdquo;.
                {linkUnassignedToDashboard && (
                  <>
                    {" "}All of them are listed under Needs you on your{" "}
                    <Link to="/dashboard" className="text-link underline underline-offset-2">Dashboard</Link>.
                  </>
                )}
              </>
            )}
            {scope.kind === "artist" && (
              <>
                Showing <strong className="font-medium text-foreground">{selectedName}&rsquo;s appointments</strong> only.
                {" "}Requests with no artist aren&rsquo;t shown here
                {linkUnassignedToDashboard ? (
                  <>
                    ; they&rsquo;re listed under Needs you on your{" "}
                    <Link to="/dashboard" className="text-link underline underline-offset-2">Dashboard</Link>.
                  </>
                ) : "."}
              </>
            )}
          </span>
        </p>
      </div>
    </div>
  );
}
