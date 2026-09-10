import { useMemo, useState } from "react";
import { ListOrdered } from "lucide-react";
import { Badge } from "@/shared/components/ui/badge";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { DataTable } from "@/shared/components/DataTable";
import { cn } from "@/shared/utils/cn";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetWaitlistQuery } from "../waitlistApi";
import { WaitlistStatus, type WaitlistEntryResponse } from "../waitlist.types";

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

const STATUS_STYLES: Record<WaitlistStatus, string> = {
  [WaitlistStatus.Waiting]:   "border-yellow-300 bg-yellow-100 text-yellow-800 hover:bg-yellow-100",
  [WaitlistStatus.Notified]:  "border-blue-300 bg-blue-100 text-blue-800 hover:bg-blue-100",
  [WaitlistStatus.Booked]:    "border-green-300 bg-green-100 text-green-800 hover:bg-green-100",
  [WaitlistStatus.Expired]:   "border-slate-300 bg-slate-100 text-slate-800 hover:bg-slate-100",
  [WaitlistStatus.Cancelled]: "border-red-300 bg-red-100 text-red-800 hover:bg-red-100",
};

function StatusBadge({ status }: { status: WaitlistStatus }) {
  return <Badge variant="outline" className={cn(STATUS_STYLES[status])}>{status}</Badge>;
}

/** Studio-wide waitlist queue for artists (auto-scoped to their own matches server-side) and
 * owners/admins (see every entry). Same page for both roles — GetWaitlistQuery handles the
 * artist self-scoping, so there's no distinct artist-facing view to maintain separately. */
export function WaitlistQueuePage() {
  useDocumentMeta({ title: "Waitlist — TattooOS", canonical: "/waitlist" });

  const { data: entries, isLoading, isError } = useGetWaitlistQuery();
  const [statusFilter, setStatusFilter] = useState<WaitlistStatus | null>(null);

  const filtered = useMemo(
    () => (statusFilter ? (entries ?? []).filter((e) => e.status === statusFilter) : entries ?? []),
    [entries, statusFilter],
  );

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <div className="flex items-center gap-2">
          <ListOrdered className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Waitlist</span>
        </div>
        {entries && (
          <span className="text-xs text-muted-foreground">
            {entries.length} entr{entries.length !== 1 ? "ies" : "y"}
          </span>
        )}
      </header>

      <main className="max-w-4xl mx-auto px-4 py-6 space-y-4">
        {!isLoading && !isError && entries && entries.length > 0 && (
          <div className="flex flex-wrap items-center gap-2" aria-label="Filter by status">
            {Object.values(WaitlistStatus).map((s) => (
              <button
                key={s}
                type="button"
                aria-pressed={statusFilter === s}
                onClick={() => setStatusFilter(statusFilter === s ? null : s)}
                className={cn(
                  "rounded-full border px-3 py-0.5 text-xs font-medium transition-colors",
                  statusFilter === s
                    ? "border-foreground bg-foreground text-background"
                    : "border-border bg-background text-muted-foreground hover:border-foreground hover:text-foreground",
                )}
              >
                {s}
              </button>
            ))}
          </div>
        )}

        {isLoading && (
          <div className="space-y-3" aria-label="Loading waitlist">
            {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-14 w-full rounded-lg" />)}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive-text py-16">
            Failed to load the waitlist. Please try again.
          </p>
        )}

        {!isLoading && !isError && entries?.length === 0 && (
          <div className="flex flex-col items-center gap-2 py-20 text-center">
            <ListOrdered className="h-10 w-10 text-muted-foreground/50" />
            <p className="text-sm font-medium text-foreground">No one is on the waitlist</p>
            <p className="text-xs text-muted-foreground">
              Clients who request an unavailable slot can join here — you'll see them as they come in.
            </p>
          </div>
        )}

        {!isLoading && !isError && filtered.length > 0 && (
          <DataTable<WaitlistEntryResponse>
            columns={[
              {
                header: "Client",
                cell: (e) => (
                  <span className="text-sm font-medium">
                    {e.clientName ?? e.guestName ?? <span className="text-muted-foreground">Guest</span>}
                  </span>
                ),
              },
              { header: "Artist", cell: (e) => e.artistName ?? "Any artist" },
              {
                header: "Preferred window",
                cell: (e) => (
                  <span className="text-sm text-muted-foreground">
                    {formatDate(e.preferredDateFrom)} – {formatDate(e.preferredDateTo)}
                  </span>
                ),
              },
              { header: "Status", cell: (e) => <StatusBadge status={e.status} /> },
              { header: "Joined", cell: (e) => formatDate(e.createdAt) },
            ]}
            data={filtered}
            keyExtractor={(e) => e.id}
            emptyMessage="No entries match this filter."
            mobileCard={(e) => (
              <div className="space-y-1">
                <div className="flex items-center justify-between gap-2">
                  <span className="text-sm font-medium truncate">
                    {e.clientName ?? e.guestName ?? "Guest"}
                  </span>
                  <StatusBadge status={e.status} />
                </div>
                <div className="flex items-center justify-between gap-2 text-sm text-muted-foreground">
                  <span>{e.artistName ?? "Any artist"}</span>
                  <span>{formatDate(e.preferredDateFrom)} – {formatDate(e.preferredDateTo)}</span>
                </div>
              </div>
            )}
          />
        )}
      </main>
    </div>
  );
}
