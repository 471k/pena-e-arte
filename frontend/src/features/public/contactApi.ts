import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";

export interface SubmitContactRequest {
  name: string;
  email: string;
  message: string;
}

// Public, anonymous contact-form submission. The backend relays it to support by email and
// persists nothing.
export const contactApi = createApi({
  reducerPath: "contactApi",
  baseQuery,
  endpoints: (builder) => ({
    submitContact: builder.mutation<void, SubmitContactRequest>({
      query: (body) => ({ url: "contact", method: "POST", body }),
    }),
  }),
});

export const { useSubmitContactMutation } = contactApi;
