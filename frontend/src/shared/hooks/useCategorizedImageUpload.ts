import { useEffect, useRef, useState } from "react";
import { generateUuid } from "@/shared/utils/uuid";
import type { CategorizedImage } from "@/features/appointments/components/CategorizedImagesField";

// Exported so callers building an R2 object key (which needs the file extension) share this
// exact mapping instead of redeclaring their own copy — BookAppointmentForm.tsx and
// GuestBookAppointmentForm.tsx each had their own identical copy before this extraction.
export const ACCEPTED_IMAGE_TYPES: Record<string, string> = {
  "image/jpeg": "jpg",
  "image/png":  "png",
  "image/webp": "webp",
};

interface UseCategorizedImageUploadArgs {
  maxImages: number;
  /** Uploads one file and resolves to its public URL, or null on failure. Callers supply the
   *  actual mechanism (authenticated files/presign vs. anonymous guest presign) — this hook
   *  only owns the picked-files queue, preview URLs, and per-image status. */
  upload: (file: File) => Promise<string | null>;
}

/**
 * One category's worth of image state + upload handling — shared by BookAppointmentForm's
 * useCategorizedImageUpload and GuestBookAppointmentForm's useGuestImageUpload, which were
 * near-identical copies differing only in the actual upload call. Found via /code-review,
 * 2026-09-01. Files upload in parallel (Promise.all over the picked batch) rather than
 * serially — a guest/client attaching several photos was previously waiting roughly N× one
 * file's round-trip latency instead of ~1×.
 */
export function useCategorizedImageUpload({ maxImages, upload }: UseCategorizedImageUploadArgs) {
  const [images, setImages] = useState<CategorizedImage[]>([]);
  const [error, setError]   = useState<string | null>(null);

  // Mirrors `images` every render so the unmount-only cleanup below revokes whatever was
  // actually picked, not the `[]` it closed over at mount — a `[]`-deps effect can't depend on
  // `images` without re-running on every change, so a ref is the correct way to read the latest
  // value at unmount time. The sync itself must happen in an effect (no deps array, so it runs
  // after every render) rather than during render — mutating a ref while rendering breaks React
  // Compiler's memoization assumptions (react-hooks/refs lint rule).
  const imagesRef = useRef(images);
  useEffect(() => {
    imagesRef.current = images;
  });

  useEffect(() => () => {
    imagesRef.current.forEach((img) => URL.revokeObjectURL(img.previewUrl));
  }, []);

  async function pick(fileList: FileList | null) {
    if (!fileList || fileList.length === 0) return;
    setError(null);

    const room = maxImages - images.length;
    const picked = Array.from(fileList);
    if (picked.length > room) {
      setError(`You can attach up to ${maxImages} images.`);
    }

    const accepted = picked.slice(0, Math.max(room, 0)).filter((file) => {
      if (ACCEPTED_IMAGE_TYPES[file.type]) return true;
      setError("Only JPEG, PNG, and WebP images are accepted.");
      return false;
    });

    const entries = accepted.map((file) => ({
      file, id: generateUuid(), previewUrl: URL.createObjectURL(file),
    }));

    setImages((prev) => [
      ...prev,
      ...entries.map(({ id, previewUrl }) => ({ id, previewUrl, status: "uploading" as const, publicUrl: null })),
    ]);

    await Promise.all(entries.map(async ({ file, id }) => {
      const publicUrl = await upload(file);
      setImages((prev) => prev.map((img) => {
        if (img.id !== id) return img;
        return publicUrl
          ? { ...img, status: "done", publicUrl }
          : { ...img, status: "error" };
      }));
    }));
  }

  function remove(id: string) {
    setImages((prev) => {
      const target = prev.find((img) => img.id === id);
      if (target) URL.revokeObjectURL(target.previewUrl);
      return prev.filter((img) => img.id !== id);
    });
  }

  function clear() {
    images.forEach((img) => URL.revokeObjectURL(img.previewUrl));
    setImages([]);
    setError(null);
  }

  const uploading = images.some((img) => img.status === "uploading");
  const doneUrls = () => images.filter((img) => img.status === "done" && img.publicUrl)
    .map((img) => img.publicUrl as string);

  return { images, error, pick, remove, clear, uploading, doneUrls };
}
