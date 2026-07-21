import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";

export const helpApi = createApi({
  reducerPath: "helpApi",
  baseQuery,
  endpoints: (builder) => ({
    logHelpSearch: builder.mutation<void, { query: string; resultCount: number }>({
      query: (body) => ({ url: "help/search-log", method: "POST", body }),
    }),
  }),
});

export const { useLogHelpSearchMutation } = helpApi;
