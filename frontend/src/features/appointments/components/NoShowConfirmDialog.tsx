import { useRef, useState } from "react";
import type { ReactNode } from "react";
import { Loader2, RotateCw } from "lucide-react";
import {
  AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/shared/components/ui/alert-dialog";
import { Button } from "@/shared/components/ui/button";
import { formatSessionSpan } from "@/shared/utils/studioDay";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import { useGetNoShowPreviewQuery, useMarkNoShowMutation } from "../appointmentsApi";
import { NoShowOutcome } from "../appointment.types";
import type { AppointmentResponse, NoShowResultResponse } from "../appointment.types";
import { previewCopy, resultCopy } from "./noShowCopy";

// A04: what marking a no-show will do to the deposit, shown BEFORE it is committed, from the server's
// own decision (GET /appointments/{id}/no-show-preview, the same NoShowOutcome the commit applies).
// Nothing about money is worked out here: the amount, currency and outcome all come from the server.
// Confirm stays disabled until the preview has loaded, and whenever it fails or the outcome is Unknown.
// After the commit the dialog shows what the server says it actually did, not the projection.
// No-show cannot be undone in the product, and no undo is offered.

function errorStatus(error: unknown): number | undefined {
  if (typeof error === "object" && error !== null && "status" in error) {
    const status = (error as { status: unknown }).status;
    return typeof status === "number" ? status : undefined;
  }
  return undefined;
}

interface NoShowConfirmDialogProps {
  appointment: AppointmentResponse;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function NoShowConfirmDialog({ appointment, open, onOpenChange }: NoShowConfirmDialogProps) {
  const cancelRef = useRef<HTMLButtonElement>(null);
  const { data: studio } = useGetMyStudioQuery();
  const preview = useGetNoShowPreviewQuery(appointment.id, { skip: !open, refetchOnMountOrArgChange: true });
  const [markNoShow, { isLoading: committing }] = useMarkNoShowMutation();
  const [result, setResult] = useState<NoShowResultResponse | null>(null);
  const [commitFailed, setCommitFailed] = useState(false);

  // Each opening starts fresh (adjusted during render, not in an effect).
  const [prevOpen, setPrevOpen] = useState(open);
  if (open !== prevOpen) {
    setPrevOpen(open);
    if (open) { setResult(null); setCommitFailed(false); }
  }

  const data = preview.data;
  const known = !!data && data.canMarkNoShow && data.outcome !== NoShowOutcome.Unknown;
  const canConfirm = known && !preview.isFetching && !committing && result === null;
  const who = appointment.clientName ?? "this client";

  async function handleConfirm() {
    setCommitFailed(false);
    const r = await markNoShow(appointment.id);
    if ("data" in r && r.data) setResult(r.data);
    else setCommitFailed(true);
  }

  let body: ReactNode;
  if (result) {
    body = <p role="status" className="text-sm">{resultCopy(result)}</p>;
  } else if (preview.isFetching && !data) {
    body = (
      <p className="flex items-center gap-2 text-sm text-muted-foreground" role="status">
        <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
        Checking what would happen to the deposit…
      </p>
    );
  } else if (preview.isError && errorStatus(preview.error) === 404) {
    // Phi, 2026-11-05: an artist may only mark their own sessions as a no-show.
    body = (
      <p role="alert" className="text-sm text-destructive-text">
        Only the artist assigned to this session, or the studio owner, can mark it as a no-show.
      </p>
    );
  } else if (preview.isError || !data) {
    body = (
      <div className="space-y-2" role="alert">
        <p className="text-sm text-destructive-text">
          Couldn&apos;t check what would happen to the deposit, so this can&apos;t be confirmed right now.
          Try again, or check the payment with the studio owner.
        </p>
        <Button type="button" variant="outline" size="sm" className="gap-1.5" onClick={() => void preview.refetch()}>
          <RotateCw className="h-3.5 w-3.5" aria-hidden="true" />
          Try again
        </Button>
      </div>
    );
  } else if (!data.canMarkNoShow) {
    body = <p role="alert" className="text-sm text-destructive-text">{data.message ?? "This appointment can't be marked as a no-show."}</p>;
  } else if (data.outcome === NoShowOutcome.Unknown) {
    body = (
      <p role="alert" className="text-sm text-destructive-text">
        The deposit is in a state TattooOS can&apos;t act on, so what a no-show would do to it can&apos;t be worked out.
        Check the payment with the studio owner before marking this a no-show.
      </p>
    );
  } else {
    body = <p className="text-sm">{previewCopy(data.outcome, data.amount, data.currency)}</p>;
  }

  return (
    <AlertDialog open={open} onOpenChange={onOpenChange}>
      <AlertDialogContent
        onOpenAutoFocus={(e) => { e.preventDefault(); cancelRef.current?.focus(); }}
      >
        <AlertDialogHeader>
          <AlertDialogTitle>Mark {who} as a no-show?</AlertDialogTitle>
          <AlertDialogDescription>
            {formatSessionSpan(appointment.date, appointment.endDate, studio?.timezone)}
          </AlertDialogDescription>
        </AlertDialogHeader>

        <div className="space-y-3" aria-live="polite">
          {body}
          {!result && (
            <p className="text-sm font-medium">
              This can&apos;t be undone: there is no way to reverse a no-show afterwards.
            </p>
          )}
          {commitFailed && (
            <p role="alert" className="text-sm text-destructive-text">
              The no-show couldn&apos;t be confirmed. Check the appointment&apos;s status before trying again.
            </p>
          )}
        </div>

        <AlertDialogFooter>
          <AlertDialogCancel ref={cancelRef}>{result ? "Close" : "Keep the appointment"}</AlertDialogCancel>
          {!result && (
            <Button type="button" variant="destructive" disabled={!canConfirm} onClick={() => void handleConfirm()}>
              {committing ? <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> : null}
              Mark no-show
            </Button>
          )}
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
