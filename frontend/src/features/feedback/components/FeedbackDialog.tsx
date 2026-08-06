import { useEffect, useRef, useState } from "react";
import { useForm, Controller } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { AlertCircle, CheckCircle, FileVideo, ImageUp, Loader2, X } from "lucide-react";
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
import { Input } from "@/shared/components/ui/input";
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
import { FEEDBACK_TYPE } from "../feedback.types";
import { useSubmitFeedbackMutation } from "../feedbackApi";

const schema = z.object({
  type:  z.enum(["BugReport", "FeatureRequest", "General"]),
  title: z.string().min(1, "Title is required").max(150, "Max 150 characters"),
  body:  z.string().min(10, "Please describe in at least 10 characters").max(2000, "Max 2000 characters"),
});
type FormValues = z.infer<typeof schema>;

// Mirrors SubmitFeedbackValidator.cs's MaxAttachments.
const MAX_ATTACHMENTS = 3;
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

interface FeedbackDialogProps {
  open:         boolean;
  onOpenChange: (open: boolean) => void;
}

export function FeedbackDialog({ open, onOpenChange }: FeedbackDialogProps) {
  const [submitted, setSubmitted] = useState(false);
  const [submitFeedback, { isLoading }] = useSubmitFeedbackMutation();

  const [uploadSessionId] = useState(() => generateUuid());
  const { upload: uploadFile } = usePresignedUpload();
  const [attachments, setAttachments] = useState<Attachment[]>([]);
  const [attachmentError, setAttachmentError] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  useEffect(() => () => {
    attachments.forEach((a) => { if (a.previewUrl) URL.revokeObjectURL(a.previewUrl); });
    // Only revoke on unmount — not on every `attachments` change.
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
    defaultValues: { type: "BugReport", title: "", body: "" },
  });

  const bodyLength = watch("body").length;
  const anyUploading = attachments.some((a) => a.status === "uploading");

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

      const objectKey = `feedback/pending/${uploadSessionId}/${Date.now()}-${id}.${ext}`;
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
    try {
      await submitFeedback({
        ...values,
        ...(attachmentUrls.length > 0 ? { attachmentUrls } : {}),
      }).unwrap();
      setSubmitted(true);
      reset();
      clearAttachments();
    } catch {
      toast.error("Failed to submit. Please try again.");
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

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Send Feedback</DialogTitle>
          <DialogDescription>
            Report a bug, request a feature, or share your thoughts.
            Our team reviews every submission.
          </DialogDescription>
        </DialogHeader>

        {submitted ? (
          <div className="flex flex-col items-center gap-3 py-6 text-center">
            <CheckCircle className="h-10 w-10 text-green-500" />
            <p className="text-sm font-medium">Thank you for your feedback!</p>
            <p className="text-xs text-muted-foreground">
              We&apos;ve received your message and will review it soon.
            </p>
            <Button size="sm" onClick={() => handleClose(false)} className="mt-2">
              Close
            </Button>
          </div>
        ) : (
          <form onSubmit={handleSubmit(onSubmit)} className="space-y-4 pt-1">
            <div className="space-y-1.5">
              <Label htmlFor="feedback-type">Type</Label>
              <Controller
                control={control}
                name="type"
                render={({ field }) => (
                  <Select value={field.value} onValueChange={field.onChange}>
                    <SelectTrigger id="feedback-type">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value={FEEDBACK_TYPE.BugReport}>🐛 Bug Report</SelectItem>
                      <SelectItem value={FEEDBACK_TYPE.FeatureRequest}>✨ Feature Request</SelectItem>
                      <SelectItem value={FEEDBACK_TYPE.General}>💬 General Feedback</SelectItem>
                    </SelectContent>
                  </Select>
                )}
              />
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="feedback-title">Title</Label>
              <Input
                id="feedback-title"
                placeholder="Brief summary"
                disabled={isLoading}
                {...register("title")}
                className={cn(errors.title && "border-destructive")}
              />
              {errors.title && (
                <p className="text-xs text-destructive">{errors.title.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <div className="flex items-center justify-between">
                <Label htmlFor="feedback-body">Description</Label>
                <span className={cn(
                  "text-xs",
                  bodyLength > 1800 ? "text-amber-500" : "text-muted-foreground"
                )}>
                  {bodyLength}/2000
                </span>
              </div>
              <Textarea
                id="feedback-body"
                rows={5}
                placeholder="Describe the issue or idea in detail…"
                disabled={isLoading}
                {...register("body")}
                className={cn("resize-none", errors.body && "border-destructive")}
              />
              {errors.body && (
                <p className="text-xs text-destructive">{errors.body.message}</p>
              )}
            </div>

            <div className="space-y-1.5">
              <Label htmlFor="feedback-attachments">
                Attachments <span className="text-muted-foreground font-normal">(optional)</span>
              </Label>
              <div
                role="button"
                tabIndex={attachments.length >= MAX_ATTACHMENTS || isLoading ? -1 : 0}
                onClick={() => fileRef.current?.click()}
                onKeyDown={(e) => e.key === "Enter" && fileRef.current?.click()}
                className={cn(
                  "flex flex-col items-center justify-center gap-1.5 rounded-lg border-2 border-dashed",
                  "border-input bg-background px-4 py-4 text-xs text-muted-foreground text-center",
                  "cursor-pointer hover:border-ring hover:text-foreground transition-colors",
                  (attachments.length >= MAX_ATTACHMENTS || isLoading) && "pointer-events-none opacity-50"
                )}
              >
                <ImageUp className="h-5 w-5" aria-hidden="true" />
                <span>Add a screenshot or short video (up to {MAX_ATTACHMENTS})</span>
              </div>
              <input
                ref={fileRef}
                id="feedback-attachments"
                type="file"
                accept="image/jpeg,image/png,image/webp,video/mp4,video/webm,video/quicktime"
                multiple
                className="sr-only"
                onChange={(e) => { void handlePickFiles(e.target.files); e.target.value = ""; }}
                disabled={attachments.length >= MAX_ATTACHMENTS || isLoading}
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
                disabled={isLoading}
              >
                Cancel
              </Button>
              <Button type="submit" size="sm" disabled={isLoading || anyUploading}>
                {(isLoading || anyUploading) && <Loader2 className="h-4 w-4 animate-spin mr-1.5" />}
                {anyUploading ? "Uploading…" : "Send Feedback"}
              </Button>
            </div>
          </form>
        )}
      </DialogContent>
    </Dialog>
  );
}
