import { useId, useState } from "react";
import type { ReactNode } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { toast } from "sonner";
import { AlertTriangle, ArrowLeft, Check, ChevronDown, Clock, ImageOff, Loader2, RefreshCw, Trash2, Upload } from "lucide-react";
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
import { ShowMoreText } from "@/shared/components/ShowMoreText";
import { cn } from "@/shared/utils/cn";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { useAppSelector } from "@/app/hooks";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { useGetClientByIdQuery } from "@/features/clients/clientsApi";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import {
  useDeleteRevisionMutation,
  useGetDesignQuery,
  useGetRevisionsQuery,
  useReviewRevisionMutation,
} from "../designsApi";
import type { DesignRevisionResponse } from "../design.types";
import { formatStudioDateTime, waitingMessage, type DesignViewer } from "../designWaiting";
import { DesignStatusBadge } from "./DesignStatusBadge";
import { ShareDesignButton } from "./ShareDesignButton";

// E04c (A05): every review decision names the revision it acts on (version number and upload time in
// the studio's timezone). The latest version leads; earlier ones and their feedback history sit behind
// a disclosure. Approval stays per revision on the server (POST revisions/{id}/review), and the API
// still accepts a review of any revision: the page only offers review on the latest one, so approving
// an outdated version takes deliberate effort outside the UI. Latest-only enforcement is NOT done here;
// it needs API/product agreement (flagged in the E04c report).

const notesSchema = z.object({
  notes: z.string().max(2000, "Max 2000 characters").optional(),
});
type NotesForm = z.infer<typeof notesSchema>;

function StatusBadge({ status }: { status: string | null }) {
  if (!status || status === "Pending") {
    return (
      <span className="inline-flex items-center rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
        Pending
      </span>
    );
  }
  if (status === "Approved") {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-green-500/10 px-2 py-0.5 text-xs font-medium text-success-text dark:text-green-400">
        <Check className="h-3 w-3" aria-hidden="true" />
        Approved
      </span>
    );
  }
  if (status === "Expired") {
    return (
      <span className="inline-flex items-center gap-1 rounded-full bg-muted px-2 py-0.5 text-xs font-medium text-muted-foreground">
        <Clock className="h-3 w-3" aria-hidden="true" />
        No answer in time
      </span>
    );
  }
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-amber-500/10 px-2 py-0.5 text-xs font-medium text-warning-text dark:text-amber-400">
      <RefreshCw className="h-3 w-3" aria-hidden="true" />
      Changes Requested
    </span>
  );
}

/** What happened to this revision's review, in words, with its date. */
function ReviewHistory({ revision, latestVersion, timeZone }: {
  revision: DesignRevisionResponse; latestVersion: number; timeZone: string | undefined;
}) {
  const when = revision.reviewedAt ? ` on ${formatStudioDateTime(revision.reviewedAt, timeZone)}` : "";
  const isLatest = revision.versionNumber === latestVersion;
  let line: string | null;
  switch (revision.approvalStatus) {
    case "Approved":         line = `Approved${when}.`; break;
    case "ChangesRequested": line = `Changes requested${when}.`; break;
    case "Expired":          line = `The review window closed${when} without an answer.`; break;
    default:
      line = isLatest ? null : `Not reviewed. Superseded by version ${latestVersion}.`;
  }
  if (!line && !revision.approvalNotes) return null;
  return (
    <div className="space-y-1">
      {line && <p className="text-xs text-muted-foreground">{line}</p>}
      {revision.approvalNotes && (
        <div className="border-l-2 border-amber-500 pl-2">
          <p className="text-xs font-medium">Client feedback on version {revision.versionNumber}</p>
          <ShowMoreText text={revision.approvalNotes} className="text-xs text-muted-foreground" />
        </div>
      )}
    </div>
  );
}

interface RevisionCardProps {
  revision:      DesignRevisionResponse;
  latestVersion: number;
  canReview:     boolean;
  canDelete:     boolean;
  timeZone:      string | undefined;
}

function RevisionCard({ revision, latestVersion, canReview, canDelete, timeZone }: RevisionCardProps) {
  const [mode, setMode]           = useState<"idle" | "changes">("idle");
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [review,  { isLoading }]  = useReviewRevisionMutation();
  const [deleteRevision, { isLoading: deleting }] = useDeleteRevisionMutation();
  const reviewHeadingId = useId();

  const { register, handleSubmit, reset, formState: { errors } } =
    useForm<NotesForm>({ resolver: zodResolver(notesSchema) });

  const isLatest = revision.versionNumber === latestVersion;
  const uploaded = formatStudioDateTime(revision.uploadedAt, timeZone);
  // Review is offered on the latest version only (see the file comment); the API is unchanged.
  const isReviewable = canReview && isLatest && revision.approvalStatus !== "Approved";

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
      toast.success(`Version ${revision.versionNumber} approved.`);
    } else {
      toast.error(`Failed to approve version ${revision.versionNumber}.`);
    }
  }

  async function requestChanges(values: NotesForm) {
    const result = await review({
      revisionId: revision.id,
      approved:   false,
      notes:      values.notes?.trim() || null,
    });
    if ("data" in result) {
      toast.success(`Changes requested on version ${revision.versionNumber}.`);
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
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex flex-wrap items-center gap-2">
            <span className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
              v{revision.versionNumber}
            </span>
            {isLatest && (
              <span className="inline-flex items-center rounded-full border px-2 py-0.5 text-xs font-medium">Latest</span>
            )}
            <StatusBadge status={revision.approvalStatus} />
          </div>
          <div className="flex items-center gap-2">
            <span className="text-xs text-muted-foreground">Uploaded {uploaded}</span>
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
                aria-label={`Delete revision v${revision.versionNumber}`}
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
          <ShowMoreText text={revision.notes} className="text-xs text-muted-foreground" />
        )}

        {/* What happened to this version's review, and the client's feedback on it */}
        <ReviewHistory revision={revision} latestVersion={latestVersion} timeZone={timeZone} />

        {/* Review actions (clients only, latest non-approved revision), naming the version */}
        {isReviewable && (
          <div role="group" aria-labelledby={reviewHeadingId} className="pt-1 space-y-3">
            <p id={reviewHeadingId} className="text-sm font-medium">
              Your review of version {revision.versionNumber}, uploaded {uploaded}
            </p>
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
                    What should change in version {revision.versionNumber}? (optional)
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
                    <p className="text-xs text-destructive-text">{errors.notes.message}</p>
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

/** Earlier versions behind a real disclosure: collapsed by default, and `hidden` while collapsed so
 * nothing inside is focusable or exposed to assistive technology. */
function EarlierVersions({ children, count }: { children: ReactNode; count: number }) {
  const [open, setOpen] = useState(false);
  const regionId = useId();
  return (
    <div className="space-y-3">
      <button
        type="button"
        className="flex w-full items-center justify-between rounded-md border px-3 py-2 text-sm font-medium hover:bg-muted/40 focus:outline-none focus-visible:ring-2 focus-visible:ring-ring"
        aria-expanded={open}
        aria-controls={regionId}
        onClick={() => setOpen((v) => !v)}
      >
        <span>Earlier versions ({count})</span>
        <ChevronDown className={cn("h-4 w-4 transition-transform", open && "rotate-180")} aria-hidden="true" />
      </button>
      <div id={regionId} role="region" aria-label="Earlier versions" hidden={!open} className="space-y-4">
        {children}
      </div>
    </div>
  );
}

export function DesignDetailPage() {
  const { id: designId } = useParams<{ id: string }>();
  const navigate  = useNavigate();
  const role      = useAppSelector((s) => s.auth.role);
  const canReview = role === Role.Client;
  const canUpload = usePermission(Role.Artist);
  const viewer: DesignViewer = role === Role.Client ? "client" : role === Role.Artist ? "artist" : "owner";

  const { data: design } = useGetDesignQuery(designId ?? "", { skip: !designId });
  const { data: client } = useGetClientByIdQuery(design?.clientId ?? "", { skip: !design?.clientId });
  const { data: studio } = useGetMyStudioQuery();
  const timeZone = studio?.timezone;

  useDocumentMeta({
    title:     design ? `${design.title} — Designs — TattooOS` : "Designs — TattooOS",
    canonical: `/designs/${designId ?? ""}`,
  });

  const { data: revisions, isLoading, isError, error } =
    useGetRevisionsQuery(designId ?? "", { skip: !designId });
  // 404: the design doesn't exist, or it isn't one this person may open (only its client, its artist and
  // the studio's owners can). The server deliberately answers the same for both.
  const notAvailable = isError && typeof error === "object" && error !== null && "status" in error
    && (error as { status: unknown }).status === 404;

  // Latest first; the server returns them oldest first.
  const ordered = [...(revisions ?? [])].sort((a, b) => b.versionNumber - a.versionNumber);
  const latest = ordered[0];
  const earlier = ordered.slice(1);

  const needsAttention =
    design && !design.isCatalogItem && design.waitingOn !== "Nobody"
    && ((viewer === "client") === (design.waitingOn === "Client"));

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
        {design && (
          <div className="space-y-2">
            <div className="flex items-center gap-2 flex-wrap">
              <h1 className="text-lg font-semibold leading-tight">{design.title}</h1>
              <DesignStatusBadge status={design.status} />
            </div>
            {client && (
              <p className="text-sm text-muted-foreground">
                For {client.firstName} {client.lastName}
              </p>
            )}
            {!design.isCatalogItem && (
              <div
                data-testid="design-waiting-state"
                data-waiting-on={design.waitingOn}
                className={cn(
                  "rounded-md border px-3 py-2.5 flex items-center gap-2 text-sm",
                  needsAttention
                    ? "border-amber-500/30 bg-amber-500/10 text-warning-text dark:text-amber-400"
                    : "bg-muted/40 text-muted-foreground",
                )}
              >
                {needsAttention
                  ? <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden="true" />
                  : <Clock className="h-4 w-4 shrink-0" aria-hidden="true" />}
                <span>{waitingMessage(design, viewer, latest?.approvalStatus, timeZone)}</span>
              </div>
            )}
          </div>
        )}

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

        {notAvailable && (
          <p className="text-center text-sm text-muted-foreground py-16" role="alert">
            This design isn&apos;t available. It may have been removed, or it belongs to someone else.
          </p>
        )}

        {isError && !notAvailable && (
          <p className="text-center text-sm text-destructive-text py-16">
            Failed to load revisions. Please try again.
          </p>
        )}

        {!isLoading && !isError && revisions?.length === 0 && (
          <p className="text-center text-sm text-muted-foreground py-16">
            No revisions yet.
          </p>
        )}

        {!isLoading && !isError && latest && (
          <div className="space-y-4">
            <RevisionCard
              key={latest.id}
              revision={latest}
              latestVersion={latest.versionNumber}
              canReview={canReview}
              canDelete={canUpload}
              timeZone={timeZone}
            />
            {earlier.length > 0 && (
              <EarlierVersions count={earlier.length}>
                {earlier.map((r) => (
                  <RevisionCard
                    key={r.id}
                    revision={r}
                    latestVersion={latest.versionNumber}
                    canReview={canReview}
                    canDelete={canUpload}
                    timeZone={timeZone}
                  />
                ))}
              </EarlierVersions>
            )}
          </div>
        )}
      </main>
    </div>
  );
}
