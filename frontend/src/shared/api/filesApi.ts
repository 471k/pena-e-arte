import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "./baseQuery";

export interface PresignUploadRequest {
  objectKey:   string;
  contentType: string;
}

export interface PresignUploadResponse {
  uploadUrl: string;
  publicUrl: string;
}

export const filesApi = createApi({
  reducerPath: "filesApi",
  baseQuery,
  endpoints: (builder) => ({
    presignUpload: builder.mutation<PresignUploadResponse, PresignUploadRequest>({
      query: (body) => ({ url: "files/presign", method: "POST", body }),
    }),
  }),
});

export const { usePresignUploadMutation } = filesApi;
