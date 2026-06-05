import { Building2, Loader2, PauseCircle, PlayCircle } from "lucide-react";
import { useState } from "react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { useGetStudiosQuery, useSuspendStudioMutation, useUnsuspendStudioMutation } from "@/features/studios/studiosApi";
import type { StudioResponse } from "@/features/studios/studiosApi";

function StudioRow({ studio }: { studio: StudioResponse }) {
  const [suspend,   { isLoading: suspending   }] = useSuspendStudioMutation();
  const [unsuspend, { isLoading: unsuspending }] = useUnsuspendStudioMutation();
  const [confirm, setConfirm] = useState<"suspend" | "unsuspend" | null>(null);

  const trialExpired = new Date(studio.trialExpiresAt) < new Date();

  async function execute() {
    if (confirm === "suspend")   await suspend(studio.id);
    if (confirm === "unsuspend") await unsuspend(studio.id);
    setConfirm(null);
  }

  return (
    <Card>
      <CardContent className="p-4 flex items-start justify-between gap-4">
        <div className="space-y-0.5 min-w-0">
          <div className="flex items-center gap-2">
            <span className="font-medium text-sm">{studio.name}</span>
            <span className="text-xs text-muted-foreground font-mono">{studio.slug}</span>
          </div>
          <p className="text-xs text-muted-foreground">
            {studio.city}
            {" · "}
            Registered {new Date(studio.createdAt).toLocaleDateString("en-GB")}
            {" · "}
            Trial {trialExpired ? "expired" : `expires ${new Date(studio.trialExpiresAt).toLocaleDateString("en-GB")}`}
          </p>
        </div>

        <div className="flex items-center gap-1.5 shrink-0">
          {confirm ? (
            <>
              <span className="text-xs text-muted-foreground">
                {confirm === "suspend" ? "Suspend?" : "Unsuspend?"}
              </span>
              <Button
                size="sm"
                variant={confirm === "suspend" ? "destructive" : "default"}
                className="h-7 px-2 text-xs"
                disabled={suspending || unsuspending}
                onClick={execute}
              >
                {(suspending || unsuspending) ? <Loader2 className="h-3 w-3 animate-spin" /> : "Yes"}
              </Button>
              <Button
                size="sm"
                variant="ghost"
                className="h-7 px-2 text-xs"
                onClick={() => setConfirm(null)}
              >
                No
              </Button>
            </>
          ) : (
            <Button
              size="sm"
              variant="ghost"
              className="h-7 px-2 text-xs gap-1"
              onClick={() => setConfirm(trialExpired ? "unsuspend" : "suspend")}
            >
              {trialExpired
                ? <><PlayCircle className="h-3.5 w-3.5" /> Reactivate</>
                : <><PauseCircle className="h-3.5 w-3.5" /> Suspend</>
              }
            </Button>
          )}
        </div>
      </CardContent>
    </Card>
  );
}

export function IssuerStudioListPage() {
  const { data: studios, isLoading, isError } = useGetStudiosQuery();

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-2 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Building2 className="h-5 w-5" />
        <span className="font-semibold tracking-tight">Studios</span>
        {studios && (
          <span className="ml-auto text-xs text-muted-foreground">
            {studios.length} studio{studios.length !== 1 ? "s" : ""}
          </span>
        )}
      </header>

      <main className="max-w-3xl mx-auto px-4 py-6 space-y-3">
        {isLoading && (
          <div className="flex items-center justify-center py-16 gap-2 text-muted-foreground">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading…</span>
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load studios.
          </p>
        )}

        {!isLoading && !isError && studios?.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">No studios yet.</p>
        )}

        {!isLoading && !isError && studios?.map((s) => (
          <StudioRow key={s.id} studio={s} />
        ))}
      </main>
    </div>
  );
}
