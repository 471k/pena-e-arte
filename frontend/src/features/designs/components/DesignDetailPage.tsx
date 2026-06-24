import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { toast } from "sonner";
import { ArrowLeft, Check, ImageOff, Loader2, RefreshCw, Trash2, Upload } from "lucide-react";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter,
  DialogHeader, DialogTitle,
} from "@/shared/components/ui/dialog";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import { cn } from "@/shared/utils/cn";
import { useAppSelector } from "@/app/hooks";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { useDeleteRevisionMutation, useGetRevisionsQuery, useReviewRevisionMutation } from "../designsApi";
import type { DesignRevisionResponse } from "../design.types";
import { ShareDesignButton } from "./ShareDesignButton";

const notesSchema = z.object({
  notes: z.string().max(2000, "Max 2000 characters").optional(),
});
type NotesForm = z.infer<typeof notesSchema>;

function StatusBadge({ status }: { status: string | null }) {
  if (!status) {
    return (
      <span className="inline-flex items-center rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
        Pending
      </span>
    );
  }
  if (status === "Approved") {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-green-500/15 px-2 py-0.5 text-xs font-medium text-green-600 dark:text-green-400">
        <Check className="h-3 w-3" />
        Approved
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-yellow-500/15 px-2 py-0.5 text-xs font-medium text-yellow-700 dark:text-yellow-400">
      <RefreshCw className="h-3 w-3" />
      Changes Requested
    </span>
  );
}

function formatDate(dateStr: string): string {
  return new Date(dateStr).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

interface RevisionCardProps {
  revision:   DesignRevisionResponse;
  canReview:  boolean;
  canDelete:  boolean;
}

function RevisionCard({ revision, canReview, canDelete }: RevisionCardProps) {
  const [mode, setMode]           = useState<"idle" | "changes">("idle");
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [review,  { isLoading }]  = useReviewRevisionMutation();
  const [deleteRevision, { isLoading: deleting }] = useDeleteRevisionMutation();

  const { register, handleSubmit, reset, formState: { errors } } =
    useForm<NotesForm>({ resolver: zodResolver(notesSchema) });

  const isReviewable = canReview && revision.approvalStatus !== "Approved";

  async function handleDelete() {
    const result = await deleteRevision({ designId: revision.designId, revisionId: revision.id });
    setDeleteOpen(false);
    if ("error" in result) {
      toast.error("Failed to delete revision.");
    } else {
      toast.success("Revision deleted.");
    }
  }

  async function approve() {
    const result = await review({ revisionId: revision.id, approved: true, notes: null });
    if ("data" in result) {
      toast.success("Design approved.");
    } else {
      toast.error("Failed to approve design.");
    }
  }

  async function requestChanges(values: NotesForm) {
    const result = await review({
      revisionId: revision.id,
      approved:   false,
      notes:      values.notes?.trim() || null,
    });
    if ("data" in result) {
      toast.success("Changes requested.");
      reset();
      setMode("idle");
    } else {
      toast.error("Failed to submit review.");
    }
  }

  return (
    <>
    <Card>
      <CardContent className="p-4 space-y-3">
        {/* Header row */}
        <div className="flex items-center justify-between gap-3">
          <div className="flex items-center gap-2">
            <span className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
              v{revision.versionNumber}
            </span>
            <StatusBadge status={revision.approvalStatus} />
          </div>
          <div className="flex items-center gap-2">
            <span className="text-xs text-muted-foreground">{formatDate(revision.uploadedAt)}</span>
            {canDelete && (
              <ShareDesignButton revisionId={revision.id} />
            )}
            {canDelete && (
              <Button
                variant="ghost"
                size="icon"
                className="h-6 w-6 text-muted-foreground hover:text-destructive"
                onClick={() => setDeleteOpen(true)}
                disabled={deleting}
                aria-label="Delete revision"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </Button>
            )}
          </div>
        </div>

        {/* Image */}
        <a
          href={revision.fileUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="block overflow-hidden rounded-md border border-input bg-muted"
        >
          <img
            src={revision.fileUrl}
            alt={`Revision v${revision.versionNumber}`}
            className="w-full max-h-64 object-contain"
            onError={(e) => {
              (e.currentTarget as HTMLImageElement).style.display = "none";
              (e.currentTarget.nextSibling as HTMLElement | null)?.removeAttribute("hidden");
            }}
          />
          <div hidden className="flex flex-col items-center justify-center gap-1 py-8 text-muted-foreground">
            <ImageOff className="h-6 w-6" />
            <span className="text-xs">Preview unavailable</span>
          </div>
        </a>

        {/* Upload notes */}
        {revision.notes && (
          <p className="text-xs text-muted-foreground">{revision.notes}</p>
        )}

        {/* Existing approval notes */}
        {revision.approvalNotes && (
          <p className="text-xs border-l-2 border-yellow-500 pl-2 text-muted-foreground">
            {revision.approvalNotes}
          </p>
        )}

        {/* Review actions (clients only, non-approved revisions) */}
        {isReviewable && (
          <div className="pt-1 space-y-3">
            {mode === "idle" && (
              <div className="flex gap-2">
                <Button
                  size="sm"
                  className="gap-1.5 flex-1"
                  disabled={isLoading}
                  onClick={approve}
                >
                  {isLoading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Check className="h-3.5 w-3.5" />}
                  Approve
                </Button>
                <Button
                  variant="outline"
                  size="sm"
                  className="gap-1.5 flex-1"
                  disabled={isLoading}
                  onClick={() => setMode("changes")}
                >
                  <RefreshCw className="h-3.5 w-3.5" />
                  Request Changes
                </Button>
              </div>
            )}

            {mode === "changes" && (
              <form onSubmit={handleSubmit(requestChanges)} className="space-y-2">
                <div className="space-y-1">
                  <Label htmlFor={`notes-${revision.id}`} className="text-xs">
                    Notes (optional)
                  </Label>
                  <Textarea
                    id={`notes-${revision.id}`}
                    rows={3}
                    placeholder="Describe what needs to change…"
                    disabled={isLoading}
                    {...register("notes")}
                    className={cn("resize-none", errors.notes && "border-destructive")}
                  />
                  {errors.notes && (
                    <p className="text-xs text-destructive">{errors.notes.message}</p>
                  )}
                </div>
                <div className="flex gap-2">
                  <Button type="submit" size="sm" variant="outline" className="flex-1" disabled={isLoading}>
                    {isLoading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : "Submit"}
                  </Button>
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    className="flex-1"
                    disabled={isLoading}
                    onClick={() => { setMode("idle"); reset(); }}
                  >
                    Cancel
                  </Button>
                </div>
              </form>
            )}
          </div>
        )}
      </CardContent>
    </Card>

    <Dialog open={deleteOpen} onOpenChange={setDeleteOpen}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Delete revision v{revision.versionNumber}?</DialogTitle>
          <DialogDescription>This action cannot be undone.</DialogDescription>
        </DialogHeader>
        <DialogFooter>
          <Button variant="outline" onClick={() => setDeleteOpen(false)} disabled={deleting}>
            Cancel
          </Button>
          <Button variant="destructive" onClick={handleDelete} disabled={deleting}>
            {deleting ? <Loader2 className="h-4 w-4 animate-spin" /> : "Delete"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
    </>
  );
}

export function DesignDetailPage() {
  const { id: designId } = useParams<{ id: string }>();
  const navigate  = useNavigate();
  const role      = useAppSelector((s) => s.auth.role);
  const canReview = role === Role.Client;
  const canUpload = usePermission(Role.Artist);

  const { data: revisions, isLoading, isError } =
    useGetRevisionsQuery(designId ?? "", { skip: !designId });

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center justify-between px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/designs")}
          className="gap-1.5"
        >
          <ArrowLeft className="h-4 w-4" />
          Designs
        </Button>

        {canUpload && designId && (
          <Button
            size="sm"
            variant="outline"
            className="gap-1.5"
            onClick={() => navigate(`/designs/${designId}/upload`)}
          >
            <Upload className="h-3.5 w-3.5" />
            Upload Revision
          </Button>
        )}
      </header>

      <main className="max-w-2xl mx-auto px-4 py-6 space-y-4">
        {isLoading && (
          <div className="space-y-4" aria-label="Loading revisions">
            {Array.from({ length: 3 }).map((_, i) => (
              <div key={i} className="space-y-2">
                <Skeleton className="h-48 w-full rounded-lg" />
                <Skeleton className="h-4 w-32" />
              </div>
            ))}
          </div>
        )}

        {isError && (
          <p className="text-center text-sm text-destructive py-16">
            Failed to load revisions. Please try again.
          </p>
        )}

        {!isLoading && !isError && revisions?.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">
            No revisions yet.
          </p>
        )}

        {!isLoading && !isError && revisions && revisions.length > 0 && (
          <div className="space-y-4">
            {revisions.map((r) => (
              <RevisionCard key={r.id} revision={r} canReview={canReview} canDelete={canUpload} />
            ))}
          </div>
        )}
      </main>
    </div>
  );
}
