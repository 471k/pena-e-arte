import { useId, useState } from "react";
import { History, Loader2, Undo2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import {
  AlertDialog, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/shared/components/ui/alert-dialog";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/shared/components/ui/dialog";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import { useRequestVersionAgainMutation, useRestoreRevisionMutation } from "../designsApi";
import type { DesignRevisionResponse } from "../design.types";

// Going back to an earlier version (Phi, 2026-11-06). A review always applies to the newest version, so
// a client who prefers an earlier one asks for it back, and the artist brings it back as the newest
// version for the client to review. Nothing here approves an older version.

const NOTE_MAX = 1900;

function serverMessage(error: unknown, fallback: string): string {
  if (typeof error === "object" && error !== null && "data" in error) {
    const data = (error as { data: unknown }).data;
    if (typeof data === "object" && data !== null) {
      const d = data as { message?: unknown; detail?: unknown };
      if (typeof d.message === "string" && d.message) return d.message;
      if (typeof d.detail === "string" && d.detail) return d.detail;
    }
  }
  return fallback;
}

/** Client, on an earlier version: ask the artist to bring it back. */
export function AskForVersionBack({ revision, latestVersion }: { revision: DesignRevisionResponse; latestVersion: number }) {
  const [open, setOpen] = useState(false);
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [ask, { isLoading }] = useRequestVersionAgainMutation();
  const noteId = useId();
  const v = revision.versionNumber;

  async function submit() {
    setError(null);
    const result = await ask({ revisionId: revision.id, notes: note.trim() || null });
    if ("data" in result) {
      toast.success(`Asked your artist to bring back version ${v}.`);
      setOpen(false);
      setNote("");
    } else {
      setError(serverMessage(result.error, `Couldn't ask for version ${v} back. Please try again.`));
    }
  }

  return (
    <>
      <Button type="button" variant="outline" size="sm" className="gap-1.5" onClick={() => { setError(null); setOpen(true); }}>
        <History className="h-3.5 w-3.5" aria-hidden="true" />
        Ask for version {v} back
      </Button>
      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Ask for version {v} back?</DialogTitle>
            <DialogDescription>
              Your artist is asked to bring back version {v} as the newest version, and you review it then.
              This counts as asking for changes to version {latestVersion}.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-1">
            <Label htmlFor={noteId} className="text-xs">Anything to tell your artist? (optional)</Label>
            <Textarea
              id={noteId}
              rows={3}
              maxLength={NOTE_MAX}
              value={note}
              onChange={(e) => setNote(e.target.value)}
              placeholder={`What you prefer about version ${v}…`}
              disabled={isLoading}
              className="resize-none"
            />
          </div>
          {error && <p role="alert" className="text-sm text-destructive-text">{error}</p>}
          <DialogFooter>
            <Button type="button" variant="outline" onClick={() => setOpen(false)} disabled={isLoading}>Cancel</Button>
            <Button type="button" onClick={() => void submit()} disabled={isLoading} className="gap-1.5">
              {isLoading && <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />}
              Ask for version {v} back
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}

/** Artist, on an earlier version: bring it back as the newest version. */
export function BringVersionBack({ revision, latestVersion }: { revision: DesignRevisionResponse; latestVersion: number }) {
  const [open, setOpen] = useState(false);
  const [restore, { isLoading }] = useRestoreRevisionMutation();
  const v = revision.versionNumber;
  const next = latestVersion + 1;

  async function confirm() {
    const result = await restore({ designId: revision.designId, revisionId: revision.id });
    if ("data" in result && result.data) {
      toast.success(`Version ${v} is back as version ${result.data.versionNumber}.`);
      setOpen(false);
    } else {
      toast.error(serverMessage(result.error, `Couldn't bring back version ${v}.`));
    }
  }

  return (
    <>
      <Button type="button" variant="outline" size="sm" className="gap-1.5" onClick={() => setOpen(true)}>
        <Undo2 className="h-3.5 w-3.5" aria-hidden="true" />
        Bring back as version {next}
      </Button>
      <AlertDialog open={open} onOpenChange={setOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Bring back version {v} as version {next}?</AlertDialogTitle>
            <AlertDialogDescription>
              The same image is added as the newest version for the client to review. Version {v} itself stays
              as it is.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={isLoading}>Cancel</AlertDialogCancel>
            <Button type="button" onClick={() => void confirm()} disabled={isLoading} className="gap-1.5">
              {isLoading && <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />}
              Bring back
            </Button>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </>
  );
}
