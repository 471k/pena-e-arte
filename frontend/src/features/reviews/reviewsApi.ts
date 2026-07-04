import { createApi } from "@reduxjs/toolkit/query/react";
import { baseQuery } from "@/shared/api/baseQuery";
import { publicApi } from "@/features/public/publicApi";

interface RespondToReviewArgs {
  reviewId: string;
  response: string;
}

export const reviewsApi = createApi({
  reducerPath: "reviewsApi",
  baseQuery,
  endpoints: (builder) => ({
    respondToReview: builder.mutation<void, RespondToReviewArgs>({
      query: ({ reviewId, response }) => ({
        url:    `reviews/${reviewId}/respond`,
        method: "POST",
        body:   { response },
      }),
      // reviewsApi and publicApi are separate RTK Query cache slices — invalidating
      // tags here only clears this slice, so the public review lists (a different
      // slice's cache) must be invalidated explicitly for the reply to show up.
      async onQueryStarted(_args, { dispatch, queryFulfilled }) {
        await queryFulfilled;
        dispatch(
          publicApi.util.invalidateTags(["StudioReviews", "ArtistReviews", "PortfolioImageReviews"]),
        );
      },
    }),
  }),
});

export const { useRespondToReviewMutation } = reviewsApi;
