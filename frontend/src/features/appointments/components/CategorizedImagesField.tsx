import { useRef } from "react";
import { AlertCircle, ImageUp, Loader2, RotateCw, X } from "lucide-react";
import { cn } from "@/shared/utils/cn";
import { LiveAnnouncement } from "@/shared/components/LiveAnnouncement";
import type { AppointmentAttachmentCategory } from "../appointment.types";
import { FieldLabel } from "./FieldLabel";

export interface CategorizedImage {
  id:         string;
  previewUrl: string;
  status:     "uploading" | "done" | "error";
  publicUrl:  string | null;
}

interface CategorizedImagesFieldProps {
  category:    AppointmentAttachmentCategory;
  label:       string;
  helperText:  string;
  /** C06: why the studio wants this photo, in the client's terms. Shown under the label and read with the control. */
  purpose:     string;
  required:    boolean;
  max:         number;
  images:      CategorizedImage[];
  error:       string | null;
  onPick:      (files: FileList | null) => void;
  /** C06: a failed upload can be tried again with the same file, not only removed. */
  onRetry:     (id: string) => void;
  onRemove:    (id: string) => void;
  disabled:    boolean;
}

/** Generalized from the original single-collection ReferenceImagesField — one instance per
 *  AppointmentAttachmentCategory (Decision #6).
 *
 *  C06 (slice 6d): the drop zone is a real <button>, so Space works as well as Enter (it was a div with
 *  role="button" that only handled Enter, and Space is what a screen-reader user presses on a button); a failed
 *  upload says so in text and offers Retry beside Remove (it could only be removed, with "Upload failed" in a
 *  hover-only title); the failure is announced (LiveAnnouncement, assertive: an action the person started failed);
 *  and the field says why the photo is wanted. */
export function CategorizedImagesField({
  category,
  label,
  helperText,
  purpose,
  required,
  max,
  images,
  error,
  onPick,
  onRetry,
  onRemove,
  disabled,
}: CategorizedImagesFieldProps) {
  const fileRef = useRef<HTMLInputElement>(null);
  const inputId = `images-${category}`;
  const purposeId = `${inputId}-purpose`;
  const hintId = `${inputId}-hint`;
  const atLimit = images.length >= max;
  const failed = images.filter((img) => img.status === "error").length;

  return (
    <div className="space-y-1.5">
      <FieldLabel htmlFor={inputId} required={required}>{label}</FieldLabel>
      <p id={purposeId} className="text-xs text-muted-foreground">{purpose}</p>
      {/* The visible button carries the field's id and its label, so the error summary's jump (S04) and a click on
          the label land on something a person can see and press. The file input itself is hidden and untabbable. */}
      <button
        id={inputId}
        type="button"
        onClick={() => fileRef.current?.click()}
        disabled={atLimit || disabled}
        aria-describedby={`${purposeId} ${hintId}`}
        className={cn(
          "flex w-full flex-col items-center justify-center gap-1.5 rounded-lg border-2 border-dashed",
          "border-input bg-background px-4 py-5 text-xs text-muted-foreground text-center",
          "cursor-pointer hover:border-ring hover:text-foreground transition-colors",
          "focus-visible:outline-hidden focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
          "disabled:pointer-events-none disabled:opacity-50",
        )}
      >
        <ImageUp className="h-5 w-5" aria-hidden="true" />
        <span id={hintId}>{helperText}</span>
      </button>
      <input
        ref={fileRef}
        id={`${inputId}-file`}
        // Chrome exposes a file input as a button even when it is visually hidden and untabbable; without this the
        // field had two "Area photo" buttons in the accessibility tree. The visible button stands in for it.
        aria-hidden="true"
        type="file"
        accept="image/jpeg,image/png,image/webp"
        multiple
        className="sr-only"
        tabIndex={-1}
        onChange={(e) => { onPick(e.target.files); e.target.value = ""; }}
        disabled={atLimit || disabled}
      />
      {error && (
        <p className="text-xs text-destructive-text" role="alert">{error}</p>
      )}
      <LiveAnnouncement
        politeness="assertive"
        message={failed === 0 ? "" : `${failed} ${failed === 1 ? "photo" : "photos"} in ${label} didn't upload. Use Retry to try again, or Remove.`}
      />
      {images.length > 0 && (
        <div className="grid grid-cols-4 gap-2 pt-1">
          {images.map((img) => (
            <div key={img.id} className="space-y-1">
              <div className="relative aspect-square rounded-md overflow-hidden border border-border/40 bg-muted/30">
                <img src={img.previewUrl} alt={label} className="h-full w-full object-cover" />
                {img.status === "uploading" && (
                  <div className="absolute inset-0 flex items-center justify-center bg-black/50">
                    <Loader2 className="h-4 w-4 animate-spin text-white" aria-hidden="true" />
                  </div>
                )}
                {img.status === "error" && (
                  <div className="absolute inset-0 flex items-center justify-center bg-destructive/70">
                    <AlertCircle className="h-4 w-4 text-white" aria-hidden="true" />
                  </div>
                )}
                <button
                  type="button"
                  onClick={() => onRemove(img.id)}
                  aria-label="Remove image"
                  className="absolute top-1 right-1 rounded-full bg-black/60 p-0.5
                             text-white hover:bg-black/80 transition-colors"
                >
                  <X className="h-3 w-3" aria-hidden="true" />
                </button>
              </div>
              {img.status === "error" && (
                <div className="space-y-0.5">
                  <p className="text-[11px] leading-tight text-destructive-text">Upload failed</p>
                  <button
                    type="button"
                    onClick={() => onRetry(img.id)}
                    disabled={disabled}
                    className="inline-flex min-h-6 items-center gap-1 text-[11px] font-medium text-link underline-offset-2 hover:underline disabled:opacity-50"
                  >
                    <RotateCw className="h-3 w-3" aria-hidden="true" />
                    Retry
                  </button>
                </div>
              )}
            </div>
          ))}
        </div>
      )}
    </div>
  );
}
