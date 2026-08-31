import { useRef } from "react";
import { AlertCircle, ImageUp, Loader2, X } from "lucide-react";
import { cn } from "@/shared/utils/cn";
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
  required:    boolean;
  max:         number;
  images:      CategorizedImage[];
  error:       string | null;
  onPick:      (files: FileList | null) => void;
  onRemove:    (id: string) => void;
  disabled:    boolean;
}

/** Generalized from the original single-collection ReferenceImagesField — one instance per
 *  AppointmentAttachmentCategory (Decision #6). Preserves the original upload/error/remove UX
 *  byte-for-byte, just parameterized by category/label/required/max. */
export function CategorizedImagesField({
  category,
  label,
  helperText,
  required,
  max,
  images,
  error,
  onPick,
  onRemove,
  disabled,
}: CategorizedImagesFieldProps) {
  const fileRef = useRef<HTMLInputElement>(null);
  const inputId = `images-${category}`;
  const atLimit = images.length >= max;

  return (
    <div className="space-y-1.5">
      <FieldLabel htmlFor={inputId} required={required}>{label}</FieldLabel>
      <div
        role="button"
        tabIndex={atLimit || disabled ? -1 : 0}
        onClick={() => fileRef.current?.click()}
        onKeyDown={(e) => e.key === "Enter" && fileRef.current?.click()}
        className={cn(
          "flex flex-col items-center justify-center gap-1.5 rounded-lg border-2 border-dashed",
          "border-input bg-background px-4 py-5 text-xs text-muted-foreground text-center",
          "cursor-pointer hover:border-ring hover:text-foreground transition-colors",
          (atLimit || disabled) && "pointer-events-none opacity-50"
        )}
      >
        <ImageUp className="h-5 w-5" aria-hidden="true" />
        <span>{helperText}</span>
      </div>
      <input
        ref={fileRef}
        id={inputId}
        type="file"
        accept="image/jpeg,image/png,image/webp"
        multiple
        className="sr-only"
        onChange={(e) => { onPick(e.target.files); e.target.value = ""; }}
        disabled={atLimit || disabled}
      />
      {error && (
        <p className="text-xs text-destructive" role="alert">{error}</p>
      )}
      {images.length > 0 && (
        <div className="grid grid-cols-4 gap-2 pt-1">
          {images.map((img) => (
            <div
              key={img.id}
              className="relative aspect-square rounded-md overflow-hidden border border-border/40 bg-muted/30"
            >
              <img src={img.previewUrl} alt={label} className="h-full w-full object-cover" />
              {img.status === "uploading" && (
                <div className="absolute inset-0 flex items-center justify-center bg-black/50">
                  <Loader2 className="h-4 w-4 animate-spin text-white" aria-hidden="true" />
                </div>
              )}
              {img.status === "error" && (
                <div
                  className="absolute inset-0 flex items-center justify-center bg-destructive/70"
                  title="Upload failed"
                >
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
          ))}
        </div>
      )}
    </div>
  );
}
