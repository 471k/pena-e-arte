import { toast } from "sonner";
import { Bell, X } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Badge } from "@/shared/components/ui/badge";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { cn } from "@/shared/utils/cn";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useGetMyWaitlistEntriesQuery, useCancelWaitlistEntryMutation } from "../waitlistApi";
import { WaitlistStatus } from "../waitlist.types";

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

/** A client's own waitlist entries — mirrors "My bookings" card pattern. */
export function MyWaitlistPage() {
  useDocumentMeta({ title: "My Waitlist — TattooOS", canonical: "/waitlist/mine" });

  const { data: entries, isLoading, isError } = useGetMyWaitlistEntriesQuery();
  const [cancelEntry, { isLoading: isCancelling }] = useCancelWaitlistEntryMutation();

  async function handleCancel(id: string) {
    const result = await cancelEntry(id);
    if ("data" in result) toast.success("Removed from the waitlist.");
    else toast.error("Failed to remove waitlist entry.");
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Bell className="h-5 w-5" />
        <span className="font-semibold tracking-tight">My Waitlist</span>
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-2">
        {isLoading && (
          <div className="space-y-3" aria-label="Loading your waitlist entries">
            {Array.from({ length: 2 }).map((_, i) => <Skeleton key={i} className="h-16 w-full rounded-lg" />)}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive-text py-16">
            Failed to load your waitlist entries. Please try again.
          </p>
        )}

        {!isLoading && !isError && entries?.length === 0 && (
          <div className="flex flex-col items-center gap-2 py-20 text-center">
            <Bell className="h-10 w-10 text-muted-foreground/50" />
            <p className="text-sm font-medium text-foreground">You're not on any waitlists</p>
            <p className="text-xs text-muted-foreground">
              When a slot you want isn't available, you can ask to be notified from the booking page.
            </p>
          </div>
        )}

        {!isLoading && !isError && entries?.map((entry) => (
          <div key={entry.id} className="flex items-center justify-between gap-3 rounded-lg border p-3">
            <div className="space-y-1">
              <div className="flex items-center gap-2">
                <span className="text-sm font-medium">{entry.artistName ?? "Any artist"}</span>
                <Badge variant="outline" className={cn(STATUS_STYLES[entry.status])}>{entry.status}</Badge>
              </div>
              <p className="text-xs text-muted-foreground">
                {formatDate(entry.preferredDateFrom)} – {formatDate(entry.preferredDateTo)}
              </p>
              {entry.status === WaitlistStatus.Notified && (
                <p className="text-xs text-blue-700 font-medium">
                  A slot opened up — claim it within 24 hours of being notified.
                </p>
              )}
            </div>
            {(entry.status === WaitlistStatus.Waiting || entry.status === WaitlistStatus.Notified) && (
              <Button
                variant="ghost"
                size="sm"
                className="h-8 gap-1 text-muted-foreground hover:text-destructive-text"
                disabled={isCancelling}
                onClick={() => handleCancel(entry.id)}
              >
                <X className="h-3.5 w-3.5" />
                Remove
              </Button>
            )}
          </div>
        ))}
      </main>
    </div>
  );
}
