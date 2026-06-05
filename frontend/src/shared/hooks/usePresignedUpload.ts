import { useState } from "react";
import { usePresignUploadMutation } from "@/shared/api/filesApi";

export interface UsePresignedUploadResult {
  upload: (file: File, objectKey: string) => Promise<string | null>;
  isUploading: boolean;
  uploadError: string | null;
  clearError: () => void;
}

export function usePresignedUpload(): UsePresignedUploadResult {
  const [presign] = usePresignUploadMutation();
  const [isUploading, setIsUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);

  async function upload(file: File, objectKey: string): Promise<string | null> {
    setIsUploading(true);
    setUploadError(null);
    try {
      const result = await presign({ objectKey, contentType: file.type });
      if ("error" in result) {
        setUploadError("Failed to get upload URL.");
        return null;
      }
      const putResp = await fetch(result.data.uploadUrl, {
        method:  "PUT",
        body:    file,
        headers: { "Content-Type": file.type },
      });
      if (!putResp.ok) {
        setUploadError("File upload failed.");
        return null;
      }
      return result.data.publicUrl;
    } catch {
      setUploadError("Upload failed. Please try again.");
      return null;
    } finally {
      setIsUploading(false);
    }
  }

  return { upload, isUploading, uploadError, clearError: () => setUploadError(null) };
}
