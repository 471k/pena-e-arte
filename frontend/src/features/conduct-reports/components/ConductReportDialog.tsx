import { useEffect, useRef, useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { AlertCircle, AlertTriangle, CheckCircle, FileVideo, ImageUp, Loader2, X } from "lucide-react";
import { toast } from "sonner";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogDescription,
} from "@/shared/components/ui/dialog";
import { Button } from "@/shared/components/ui/button";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/shared/components/ui/select";
import { usePresignedUpload } from "@/shared/hooks/usePresignedUpload";
import { cn } from "@/shared/utils/cn";
import { generateUuid } from "@/shared/utils/uuid";
import {
  useGetReportableArtistAppointmentsQuery,
  useGetReportableStudioAppointmentsQuery,
  useFileArtistConductReportMutation,
  useFileStudioConductReportMutation,
} from "@/features/public/publicApi";
import {
  REPORT_CATEGORY,
  REPORT_CATEGORY_LABEL,
  HIGH_SEVERITY_CATEGORIES,
  type ReportCategory,
} from "../conductReports.types";

const schema = z.object({
  category:      z.enum([
    "Scam", "SexualMisconduct", "UnsafeHygienePractices", "Harassment",
    "Discrimination", "PoorServiceQuality", "Other",
  ]),
  appointmentId: z.string().min(1, "Select which visit this relates to"),
  reason:        z.string().min(20, "Please describe the issue in at least 20 characters").max(2000, "Max 2000 characters"),
});
type FormValues = z.infer<typeof schema>;

// Mirrors FileArtistConductReportValidator.cs / FileStudioConductReportValidator.cs.
const MAX_ATTACHMENTS = 3;
// Duplicated from FeedbackDialog.tsx rather than extracted into a shared component — see
// architecture.md Decisions Log ("Client Conduct Reports" entry) for why: extraction would
// have touched FeedbackDialog.tsx too, widening this feature's diff for a UI block that isn't
// otherwise changing. FeedbackDialog.tsx remains the source of truth for this pattern; keep
// the two in sync by hand if the upload UX changes.
const ACCEPTED_TYPES: Record<string, string> = {
  "image/jpeg":      "jpg",
  "image/png":       "png",
  "image/webp":      "webp",
  "video/mp4":       "mp4",
  "video/webm":      "webm",
  "video/quicktime": "mov",
};

interface Attachment {
  id:         string;
  kind:       "image" | "video";
  fileName:   string;
  previewUrl: string | null;
  status:     "uploading" | "done" | "error";
  publicUrl:  string | null;
}

interface ConductReportTarget {
  kind: "artist" | "studio";
  slug: string;
  name: string;
}

interface ConductReportDialogProps {
  open:         boolean;
  onOpenChange: (open: boolean) => void;
  target:       ConductReportTarget;
}

const CATEGORY_ORDER: ReportCategory[] = [
  "Scam", "SexualMisconduct", "UnsafeHygienePractices", "Harassment",
  "Discrimination", "PoorServiceQuality", "Other",
];

function formatVisitDate(dateStr: string): string {
  return new Date(dateStr).toLocaleDateString("en-US", { month: "short", day: "numeric", year: "numeric" });
}

export function ConductReportDialog({ open, onOpenChange, target }: ConductReportDialogProps) {
  const [submitted, setSubmitted] = useState(false);

  const { data: studioAppointments, isLoading: loadingStudio } =
    useGetReportableStudioAppointmentsQuery(target.slug, { skip: !open || target.kind !== "studio" });
  const { data: artistAppointments, isLoading: loadingArtist } =
    useGetReportableArtistAppointmentsQuery(target.slug, { skip: !open || target.kind !== "artist" });

  const eligibleAppointments = target.kind === "studio" ? studioAppointments : artistAppointments;
  const loadingEligibility   = target.kind === "studio" ? loadingStudio : loadingArtist;

  const [fileStudioReport, { isLoading: isStudioSubmitting }] = useFileStudioConductReportMutation();
  const [fileArtistReport, { isLoading: isArtistSubmitting }] = useFileArtistConductReportMutation();
  const isSubmitting = target.kind === "studio" ? isStudioSubmitting : isArtistSubmitting;

  const [uploadSessionId] = useState(() => generateUuid());
  const { upload: uploadFile } = usePresignedUpload();
  const [attachments, setAttachments] = useState<Attachment[]>([]);
  const [attachmentError, setAttachmentError] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  useEffect(() => () => {
    attachments.forEach((a) => { if (a.previewUrl) URL.revokeObjectURL(a.previewUrl); });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const {
    register,
    control,
    handleSubmit,
    watch,
    reset,
    formState: { errors },
  } = useForm<FormValues>({
    resolver: zodResolver(schema),
    defaultValues: { category: "Other", appointmentId: "", reason: "" },
  });

  const reasonLength   = watch("reason").length;
  const selectedCategory = watch("category");
  const isHighSeverity = HIGH_SEVERITY_CATEGORIES.has(selectedCategory);
  const anyUploading   = attachments.some((a) => a.status === "uploading");

  async function handlePickFiles(fileList: FileList | null) {
    if (!fileList || fileList.length === 0) return;
    setAttachmentError(null);

    const room = MAX_ATTACHMENTS - attachments.length;
    const picked = Array.from(fileList);
    if (picked.length > room) {
      setAttachmentError(`You can attach up to ${MAX_ATTACHMENTS} files.`);
    }

    for (const file of picked.slice(0, Math.max(room, 0))) {
      const ext = ACCEPTED_TYPES[file.type];
      if (!ext) {
        setAttachmentError("Only JPEG/PNG/WebP images or MP4/WebM/MOV videos are accepted.");
        continue;
      }

      const kind = file.type.startsWith("video/") ? "video" : "image";
      const id = generateUuid();
      const previewUrl = kind === "image" ? URL.createObjectURL(file) : null;
      setAttachments((prev) => [
        ...prev,
        { id, kind, fileName: file.name, previewUrl, status: "uploading", publicUrl: null },
      ]);

      const objectKey = `conduct-reports/pending/${uploadSessionId}/${Date.now()}-${id}.${ext}`;
      const publicUrl = await uploadFile(file, objectKey);

      setAttachments((prev) => prev.map((a) => {
        if (a.id !== id) return a;
        return publicUrl ? { ...a, status: "done", publicUrl } : { ...a, status: "error" };
      }));
    }
  }

  function removeAttachment(id: string) {
    setAttachments((prev) => {
      const target = prev.find((a) => a.id === id);
      if (target?.previewUrl) URL.revokeObjectURL(target.previewUrl);
      return prev.filter((a) => a.id !== id);
    });
  }

  function clearAttachments() {
    attachments.forEach((a) => { if (a.previewUrl) URL.revokeObjectURL(a.previewUrl); });
    setAttachments([]);
    setAttachmentError(null);
  }

  async function onSubmit(values: FormValues) {
    const attachmentUrls = attachments
      .filter((a) => a.status === "done" && a.publicUrl)
      .map((a) => a.publicUrl!);

    const body = {
      appointmentId: values.appointmentId,
      category:      values.category,
      reason:        values.reason,
      ...(attachmentUrls.length > 0 ? { attachmentUrls } : {}),
    };

    try {
      if (target.kind === "studio") {
        await fileStudioReport({ slug: target.slug, body }).unwrap();
      } else {
        await fileArtistReport({ slug: target.slug, body }).unwrap();
      }
      setSubmitted(true);
      reset();
      clearAttachments();
      toast.success("Report submitted. Our platform team will review it.");
    } catch {
      toast.error("Failed to submit your report. Please try again.");
    }
  }

  function handleClose(nextOpen: boolean) {
    if (!nextOpen) {
      setSubmitted(false);
      reset();
      clearAttachments();
    }
    onOpenChange(nextOpen);
  }

  const noEligibleAppointments = !loadingEligibility && (!eligibleAppointments || eligibleAppointments.length === 0);

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Report {target.kind === "studio" ? "this studio" : "this artist"}</DialogTitle>
          <DialogDescription>
            Let us know about a serious issue with {target.name}. This is reviewed by our
            platform team, not shown publicly.
          </DialogDescription>
        </DialogHeader>

        {submitted ? (
          <div className="flex flex-col items-center gap-3 py-6 text-center">
            <CheckCircle className="h-10 w-10 text-green-500" />
            <p className="text-sm font-medium">Report submitted.</p>
            <p className="text-xs text-muted-foreground">
              Our platform team will review it. You won&apos;t see it listed anywhere in your
              account, but it has been received.
            </p>
            <Button size="sm" onClick={() => handleClose(false)} className="mt-2">
              Close
            </Button>
          </div>
        ) : (
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 pt-1">
            <div className="space-y-1.5">
              <Label htmlFor="report-category">Category</Label>
              <Controller
                control={control}
                name="category"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="report-category">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {CATEGORY_ORDER.map((value) => (
                        <SelectItem key={value} value={value}>
                          <span className="flex items-center gap-1.5">
                            {HIGH_SEVERITY_CATEGORIES.has(value) && (
                              <span
                                className="h-1.5 w-1.5 rounded-full bg-amber-500 shrink-0"
                                aria-hidden="true"
                                title="Escalated for immediate review"
                              />
                            )}
                            {REPORT_CATEGORY_LABEL[value]}
                          </span>
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                )}
              />
              {isHighSeverity && (
                <p className="flex items-start gap-1.5 text-xs text-amber-500">
                  <AlertTriangle className="h-3.5 w-3.5 shrink-0 mt-0.5" aria-hidden="true" />
                  This category is escalated immediately to our platform team.
                </p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="report-appointment">Which visit does this relate to?</Label>
              {loadingEligibility ? (
                <p className="text-xs text-muted-foreground">Loading your appointments…</p>
              ) : noEligibleAppointments ? (
                <p className="text-xs text-muted-foreground">
                  You don&apos;t have any appointments with {target.name} yet.
                </p>
              ) : (
                <Controller
                  control={control}
                  name="appointmentId"
                  render={({ field }) => (
                    <Select value={field.value} onValueChange={field.onChange}>
                      <SelectTrigger id="report-appointment">
                        <SelectValue placeholder="Select an appointment" />
                      </SelectTrigger>
                      <SelectContent>
                        {eligibleAppointments!.map((a) => (
                          <SelectItem key={a.id} value={a.id}>
                            {formatVisitDate(a.date)} — {a.status}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  )}
                />
              )}
              {errors.appointmentId && (
                <p className="text-xs text-destructive">{errors.appointmentId.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <div className="flex items-center justify-between">
                <Label htmlFor="report-reason">What happened?</Label>
                <span className={cn(
                  "text-xs",
                  reasonLength > 1800 ? "text-amber-500" : "text-muted-foreground"
                )}>
                  {reasonLength}/2000
                </span>
              </div>
              <Textarea
                id="report-reason"
                rows={5}
                placeholder="Describe what happened, in your own words…"
                disabled={isSubmitting}
                {...register("reason")}
                className={cn("resize-none", errors.reason && "border-destructive")}
              />
              {errors.reason && (
                <p className="text-xs text-destructive">{errors.reason.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="report-attachments">
                Evidence <span className="text-muted-foreground font-normal">(optional)</span>
              </Label>
              <div
                role="button"
                tabIndex={attachments.length >= MAX_ATTACHMENTS || isSubmitting ? -1 : 0}
                onClick={() => fileRef.current?.click()}
                onKeyDown={(e) => e.key === "Enter" && fileRef.current?.click()}
                className={cn(
                  "flex flex-col items-center justify-center gap-1.5 rounded-lg border-2 border-dashed",
                  "border-input bg-background px-4 py-4 text-xs text-muted-foreground text-center",
                  "cursor-pointer hover:border-ring hover:text-foreground transition-colors",
                  (attachments.length >= MAX_ATTACHMENTS || isSubmitting) && "pointer-events-none opacity-50"
                )}
              >
                <ImageUp className="h-5 w-5" aria-hidden="true" />
                <span>Add a screenshot or short video (up to {MAX_ATTACHMENTS})</span>
              </div>
              <input
                ref={fileRef}
                id="report-attachments"
                type="file"
                accept="image/jpeg,image/png,image/webp,video/mp4,video/webm,video/quicktime"
                multiple
                className="sr-only"
                onChange={(e) => { void handlePickFiles(e.target.files); e.target.value = ""; }}
                disabled={attachments.length >= MAX_ATTACHMENTS || isSubmitting}
              />
              {attachmentError && (
                <p className="text-xs text-destructive" role="alert">{attachmentError}</p>
              )}
              {attachments.length > 0 && (
                <ul className="space-y-1.5 pt-1">
                  {attachments.map((a) => (
                    <li
                      key={a.id}
                      className="flex items-center gap-2 rounded-md border border-border/40 bg-muted/30 px-2 py-1.5"
                    >
                      {a.kind === "image" && a.previewUrl ? (
                        <img src={a.previewUrl} alt="" className="h-8 w-8 rounded object-cover shrink-0" />
                      ) : (
                        <FileVideo className="h-8 w-8 p-1.5 rounded bg-muted shrink-0" aria-hidden="true" />
                      )}
                      <span className="text-xs truncate flex-1">{a.fileName}</span>
                      {a.status === "uploading" && (
                        <Loader2 className="h-3.5 w-3.5 animate-spin text-muted-foreground shrink-0" aria-hidden="true" />
                      )}
                      {a.status === "error" && (
                        <span title="Upload failed" className="shrink-0">
                          <AlertCircle className="h-3.5 w-3.5 text-destructive" aria-hidden="true" />
                        </span>
                      )}
                      <button
                        type="button"
                        onClick={() => removeAttachment(a.id)}
                        aria-label={`Remove ${a.fileName}`}
                        className="text-muted-foreground hover:text-foreground transition-colors shrink-0"
                      >
                        <X className="h-3.5 w-3.5" aria-hidden="true" />
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </div>

            <div className="flex justify-end gap-2 pt-1">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => handleClose(false)}
                disabled={isSubmitting}
              >
                Cancel
              </Button>
              <Button
                type="submit"
                size="sm"
                disabled={isSubmitting || anyUploading || noEligibleAppointments}
              >
                {(isSubmitting || anyUploading) && <Loader2 className="h-4 w-4 animate-spin mr-1.5" />}
                {anyUploading ? "Uploading…" : "Submit Report"}
              </Button>
            </div>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
