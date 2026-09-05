import { useRef, useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { ArrowLeft, ImageUp, Loader2 } from "lucide-react";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import { z } from "zod";
import { Button } from "@/shared/components/ui/button";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import { usePresignUploadMutation } from "@/shared/api/filesApi";
import { useUploadRevisionMutation } from "../designsApi";

const ACCEPTED_TYPES: Record<string, string> = {
  "image/jpeg": "jpg",
  "image/png":  "png",
  "image/webp": "webp",
};

const TEXTAREA_CLS = cn(
  "flex min-h-[100px] w-full rounded-md border border-input bg-background px-3 py-2 text-sm",
  "ring-offset-background placeholder:text-muted-foreground",
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
  "disabled:cursor-not-allowed disabled:opacity-50 resize-none"
);

const notesSchema = z.object({
  notes: z.string().max(2000, "Max 2000 characters").optional(),
});
type FormValues = z.infer<typeof notesSchema>;

type UploadStep = "idle" | "presigning" | "uploading" | "saving";

function stepLabel(step: UploadStep): string {
  switch (step) {
    case "presigning": return "Getting upload URL…";
    case "uploading":  return "Uploading file…";
    case "saving":     return "Saving revision…";
    default:           return "Upload Revision";
  }
}

// Module-level so the timestamp call stays out of the component body
function buildObjectKey(designId: string, ext: string): string {
  return `designs/${designId}/${Date.now()}.${ext}`;
}

export function UploadRevisionPage() {
  const { id: designId } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const fileRef  = useRef<HTMLInputElement>(null);

  const [file,     setFile]     = useState<File | null>(null);
  const [fileError, setFileError] = useState<string | null>(null);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [step,     setStep]     = useState<UploadStep>("idle");

  const [presign]        = usePresignUploadMutation();
  const [uploadRevision] = useUploadRevisionMutation();

  const {
    register,
    handleSubmit,
    formState: { errors },
  } = useForm<FormValues>({ resolver: zodResolver(notesSchema) });

  function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const picked = e.target.files?.[0] ?? null;
    setFileError(null);
    setUploadError(null);
    if (picked && !ACCEPTED_TYPES[picked.type]) {
      setFileError("Only JPEG, PNG, and WebP images are accepted.");
      setFile(null);
      return;
    }
    setFile(picked);
  }

  async function onSubmit(values: FormValues) {
    if (!designId) return;
    if (!file) {
      setFileError("Select an image to upload.");
      return;
    }

    setUploadError(null);

    try {
      const ext       = ACCEPTED_TYPES[file.type];
      const objectKey = buildObjectKey(designId, ext);

      // Step 1 — get presigned URL
      setStep("presigning");
      const presignResult = await presign({ objectKey, contentType: file.type });
      if ("error" in presignResult) throw new Error("presign");

      // Step 2 — PUT file directly to R2 (no auth header)
      setStep("uploading");
      const putResp = await fetch(presignResult.data.uploadUrl, {
        method:  "PUT",
        body:    file,
        headers: { "Content-Type": file.type },
      });
      if (!putResp.ok) throw new Error("upload");

      // Step 3 — register revision
      setStep("saving");
      const revResult = await uploadRevision({
        designId,
        fileUrl: presignResult.data.publicUrl,
        notes:   values.notes?.trim() || null,
      });
      if ("error" in revResult) throw new Error("save");

      navigate(`/designs/${designId}`);
    } catch (err: unknown) {
      const phase = err instanceof Error ? err.message : "unknown";
      setUploadError(
        phase === "presign" ? "Failed to get upload URL. Please try again." :
        phase === "upload"  ? "File upload failed. Please try again." :
                              "Failed to save revision. Please try again."
      );
      setStep("idle");
    }
  }

  const busy = step !== "idle";

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button
          variant="ghost"
          size="sm"
          onClick={() => navigate("/designs")}
          className="gap-1.5"
          disabled={busy}
        >
          <ArrowLeft className="h-4 w-4" />
          Designs
        </Button>
      </header>

      <main className="max-w-lg mx-auto px-4 py-8">
        <form onSubmit={handleSubmit(onSubmit)} className="space-y-5">
          <h2 className="text-base font-semibold">Upload Revision</h2>

          {/* File picker */}
          <div className="space-y-1.5">
            <Label>Image</Label>
            <div
              role="button"
              tabIndex={0}
              onClick={() => fileRef.current?.click()}
              onKeyDown={(e) => e.key === "Enter" && fileRef.current?.click()}
              className={cn(
                "flex flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed",
                "border-input bg-background px-4 py-8 text-sm text-muted-foreground",
                "cursor-pointer hover:border-ring hover:text-foreground transition-colors",
                fileError && "border-destructive",
                busy && "pointer-events-none opacity-50"
              )}
            >
              <ImageUp className="h-8 w-8" />
              {file ? (
                <span className="font-medium text-foreground">{file.name}</span>
              ) : (
                <span>Click to select — JPEG, PNG, or WebP</span>
              )}
            </div>
            <input
              ref={fileRef}
              type="file"
              accept="image/jpeg,image/png,image/webp"
              className="sr-only"
              onChange={handleFileChange}
              disabled={busy}
            />
            {fileError && (
              <p className="text-xs text-destructive-text">{fileError}</p>
            )}
          </div>

          {/* Notes */}
          <div className="space-y-1.5">
            <Label htmlFor="notes">Notes (optional)</Label>
            <textarea
              id="notes"
              rows={4}
              placeholder="Describe changes in this revision…"
              disabled={busy}
              {...register("notes")}
              className={cn(TEXTAREA_CLS, errors.notes && "border-destructive")}
            />
            {errors.notes && (
              <p className="text-xs text-destructive-text">{errors.notes.message}</p>
            )}
          </div>

          {uploadError && (
            <p className="text-sm text-destructive-text">{uploadError}</p>
          )}

          <Button type="submit" className="w-full" disabled={busy}>
            {busy ? (
              <>
                <Loader2 className="h-4 w-4 animate-spin" />
                {stepLabel(step)}
              </>
            ) : (
              "Upload Revision"
            )}
          </Button>
        </form>
      </main>
    </div>
  );
}
