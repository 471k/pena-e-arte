import { useRef } from "react";
import { FileUp, ImageUp, Loader2 } from "lucide-react";
import { Label } from "@/shared/components/ui/label";
import { cn } from "@/shared/utils/cn";
import { usePresignedUpload } from "@/shared/hooks/usePresignedUpload";

export const IMAGE_ACCEPTED_TYPES: Record<string, string> = {
  "image/jpeg": "jpg",
  "image/png":  "png",
  "image/webp": "webp",
};

export const PDF_ACCEPTED_TYPES: Record<string, string> = {
  "application/pdf": "pdf",
};

interface FileUploadFieldProps {
  acceptedTypes: Record<string, string>;
  keyPrefix: string;
  label?: string;
  disabled?: boolean;
  onUploaded: (url: string) => void;
  error?: string | null;
}

export function FileUploadField({
  acceptedTypes,
  keyPrefix,
  label,
  disabled,
  onUploaded,
  error,
}: FileUploadFieldProps) {
  const fileRef = useRef<HTMLInputElement>(null);
  const { upload, isUploading, uploadError, clearError } = usePresignedUpload();

  const acceptString = Object.keys(acceptedTypes).join(",");
  const isPdf = "application/pdf" in acceptedTypes;

  async function handleChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0] ?? null;
    if (fileRef.current) fileRef.current.value = "";
    if (!file) return;

    clearError();
    const ext = acceptedTypes[file.type];
    if (!ext) return;

    const objectKey = `${keyPrefix}/${Date.now()}.${ext}`;
    const url = await upload(file, objectKey);
    if (url) onUploaded(url);
  }

  const displayError = error ?? uploadError;
  const busy = disabled || isUploading;

  return (
    <div className="space-y-1.5">
      {label && <Label>{label}</Label>}
      <div
        role="button"
        tabIndex={busy ? -1 : 0}
        onClick={() => !busy && fileRef.current?.click()}
        onKeyDown={(e) => e.key === "Enter" && !busy && fileRef.current?.click()}
        className={cn(
          "flex flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed",
          "border-input bg-background px-4 py-6 text-sm text-muted-foreground",
          "transition-colors",
          !busy && "cursor-pointer hover:border-ring hover:text-foreground",
          displayError && "border-destructive",
          busy && "opacity-50 cursor-not-allowed",
        )}
      >
        {isUploading ? (
          <>
            <Loader2 className="h-6 w-6 animate-spin" />
            <span>Uploading…</span>
          </>
        ) : isPdf ? (
          <>
            <FileUp className="h-6 w-6" />
            <span>Click to select — PDF</span>
          </>
        ) : (
          <>
            <ImageUp className="h-6 w-6" />
            <span>Click to select — JPEG, PNG, or WebP</span>
          </>
        )}
      </div>
      <input
        ref={fileRef}
        type="file"
        accept={acceptString}
        className="sr-only"
        onChange={handleChange}
        disabled={busy}
      />
      {displayError && (
        <p className="text-xs text-destructive">{displayError}</p>
      )}
    </div>
  );
}
