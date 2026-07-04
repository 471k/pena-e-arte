import { useState, useEffect } from "react";
import { Link } from "react-router-dom";
import { MessageSquare, CheckCircle, BadgeCheck, Star } from "lucide-react";
import { Button }   from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { StarRating, InteractiveStarRating } from "@/shared/components/ui/StarRating";
import { useRespondToReviewMutation } from "@/features/reviews/reviewsApi";
import {
  useGetStudioReviewsQuery,
  useGetArtistReviewsQuery,
  useGetPortfolioImageReviewsQuery,
  useCreateStudioReviewMutation,
  useCreateArtistReviewMutation,
  useCreatePortfolioImageReviewMutation,
  type ReviewResponse,
} from "../publicApi";

const PAGE_SIZE = 10;

function OwnerReplyForm({ reviewId }: { reviewId: string }) {
  const [expanded, setExpanded] = useState(false);
  const [text, setText]         = useState("");
  const [respond, { isLoading }] = useRespondToReviewMutation();

  if (!expanded) {
    return (
      <button
        type="button"
        onClick={() => setExpanded(true)}
        className="text-xs text-violet-400 hover:text-violet-300 transition-colors
                   underline underline-offset-2"
      >
        Reply
      </button>
    );
  }

  function handleSubmit() {
    if (text.trim().length === 0) return;
    void respond({ reviewId, response: text.trim() }).unwrap().then(() => {
      setExpanded(false);
      setText("");
    });
  }

  return (
    <div className="space-y-2 pt-1">
      <textarea
        aria-label="Write a reply"
        className="w-full min-h-[60px] resize-none rounded-md border bg-background px-3 py-2 text-sm
                   focus:outline-none focus:ring-1 focus:ring-ring placeholder:text-muted-foreground"
        placeholder="Thank the client or address their feedback…"
        maxLength={2000}
        value={text}
        onChange={(e) => setText(e.target.value)}
      />
      <div className="flex items-center gap-2">
        <Button
          size="sm"
          onClick={handleSubmit}
          disabled={isLoading || text.trim().length === 0}
          className="bg-violet-600 hover:bg-violet-700 text-white disabled:opacity-50"
        >
          {isLoading ? "Posting…" : "Post reply"}
        </Button>
        <button
          type="button"
          onClick={() => { setExpanded(false); setText(""); }}
          className="text-xs text-muted-foreground hover:text-foreground transition-colors"
        >
          Cancel
        </button>
      </div>
    </div>
  );
}

function ReviewCard({ review, canRespond }: { review: ReviewResponse; canRespond?: boolean }) {
  return (
    <div className="py-4 border-b last:border-b-0 space-y-2">
      <div className="flex items-start justify-between gap-2 flex-wrap">
        <div className="space-y-0.5">
          <div className="flex items-center gap-1.5">
            <span className="text-sm font-medium">{review.authorName}</span>
            {review.isVerifiedBooking && (
              <span
                className="inline-flex items-center gap-0.5
                           text-[10px] font-medium text-violet-400
                           px-1.5 py-0.5 rounded-full
                           bg-violet-500/10 border border-violet-500/20"
                title="This reviewer booked at this studio"
              >
                <BadgeCheck className="h-2.5 w-2.5" aria-hidden="true" />
                Verified client
              </span>
            )}
          </div>
          <StarRating value={review.rating} size="sm" />
        </div>
        <span className="text-xs text-muted-foreground shrink-0">
          {new Date(review.createdAt).toLocaleDateString("en-US", {
            month: "short", day: "numeric", year: "numeric",
          })}
        </span>
      </div>
      <p className="text-sm text-muted-foreground whitespace-pre-wrap leading-relaxed">
        {review.body}
      </p>

      {review.ownerResponse && (
        <div className="ml-4 mt-2 pl-3 border-l-2 border-border/50 space-y-1">
          <p className="text-[11px] font-medium text-muted-foreground uppercase tracking-wide">
            Studio response
            {review.ownerResponseAt && (
              <span className="font-normal ml-1">
                · {new Date(review.ownerResponseAt).toLocaleDateString("en-US", {
                    month: "short", day: "numeric", year: "numeric",
                  })}
              </span>
            )}
          </p>
          <p className="text-sm text-muted-foreground/90 leading-relaxed whitespace-pre-wrap">
            {review.ownerResponse}
          </p>
        </div>
      )}

      {canRespond && !review.ownerResponse && (
        <OwnerReplyForm reviewId={review.id} />
      )}
    </div>
  );
}

function ReviewsSkeleton() {
  return (
    <div className="space-y-4" aria-label="Loading reviews">
      {Array.from({ length: 3 }).map((_, i) => (
        <div key={i} className="py-4 border-b space-y-2">
          <div className="flex items-center justify-between">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-3.5 w-20" />
          </div>
          <Skeleton className="h-12 w-full" />
        </div>
      ))}
    </div>
  );
}

interface ReviewFormProps {
  slug:    string;
  token:   string | null;
  target:  "studio" | "artist" | "tattoo";
  imageId?: string;
}

function ReviewForm({ slug, token, target, imageId }: ReviewFormProps) {
  const [rating,  setRating]  = useState(0);
  const [body,    setBody]    = useState("");
  const [error,   setError]   = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

  const [createStudioReview,         { isLoading: isStudioSubmitting }]  = useCreateStudioReviewMutation();
  const [createArtistReview,         { isLoading: isArtistSubmitting }]  = useCreateArtistReviewMutation();
  const [createPortfolioImageReview, { isLoading: isTattooSubmitting }]  = useCreatePortfolioImageReviewMutation();

  const isSubmitting = target === "studio"
    ? isStudioSubmitting
    : target === "artist"
    ? isArtistSubmitting
    : isTattooSubmitting;

  useEffect(() => {
    if (!success) return;
    const id = window.setTimeout(() => setSuccess(false), 4000);
    return () => window.clearTimeout(id);
  }, [success]);

  function handleSubmit() {
    if (rating === 0) { setError("Please select a star rating."); return; }
    if (body.trim().length < 10) { setError("Review must be at least 10 characters."); return; }

    setError(null);

    const promise = target === "studio"
      ? createStudioReview({ slug, rating, body: body.trim() }).unwrap()
      : target === "artist"
      ? createArtistReview({ slug, rating, body: body.trim() }).unwrap()
      : createPortfolioImageReview({ imageId: imageId ?? "", rating, body: body.trim() }).unwrap();

    promise
      .then(() => {
        setSuccess(true);
        setBody("");
        setRating(0);
      })
      .catch((err: { status?: number }) => {
        if (err.status === 409) {
          setError("You have already left a review.");
        } else {
          setError("Failed to submit review. Please try again.");
        }
      });
  }

  if (success) {
    return (
      <div
        role="status"
        aria-live="polite"
        className="flex items-center gap-2.5 rounded-lg border border-green-800/60
                   bg-green-950/30 px-4 py-3"
      >
        <CheckCircle className="h-4 w-4 shrink-0 text-green-400" aria-hidden="true" />
        <p className="text-sm text-green-400">
          Review submitted — thank you!
        </p>
      </div>
    );
  }

  if (!token) {
    const returnUrl = target === "studio"
      ? `/s/${slug}`
      : target === "artist"
      ? `/artist/${slug}`
      : `/discover`;
    return (
      <div
        className="rounded-lg border bg-muted/20 px-5 py-6
                   flex flex-col items-center gap-3 text-center"
      >
        <p className="text-sm text-muted-foreground">
          Sign in to share your experience with this {target === "tattoo" ? "tattoo" : target}.
        </p>
        <Button size="sm" asChild>
          <Link to={`/login?redirect=${encodeURIComponent(returnUrl)}`}>
            Sign in to leave a review
          </Link>
        </Button>
      </div>
    );
  }

  return (
    <div className="rounded-lg border p-4 space-y-3 bg-muted/30">
      <label htmlFor="review-body" className="text-sm font-medium">
        Write a review
      </label>

      <InteractiveStarRating
        value={rating}
        onChange={(r) => { setRating(r); setError(null); }}
      />

      <textarea
        id="review-body"
        aria-label="Write a review"
        className="w-full min-h-[80px] resize-none rounded-md border bg-background px-3 py-2 text-sm
                   focus:outline-none focus:ring-1 focus:ring-ring placeholder:text-muted-foreground"
        placeholder="How was the experience? Quality of the work, cleanliness, communication…"
        maxLength={2000}
        value={body}
        onChange={(e) => setBody(e.target.value)}
        onKeyDown={(e) => {
          if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            handleSubmit();
          }
        }}
      />

      {error && (
        <p className="text-xs text-destructive" role="alert">{error}</p>
      )}

      <Button
        size="sm"
        onClick={handleSubmit}
        disabled={isSubmitting || rating === 0}
        aria-label="Post review"
        className="bg-violet-600 hover:bg-violet-700 text-white
                   disabled:opacity-50 disabled:cursor-not-allowed"
      >
        {isSubmitting ? "Posting…" : "Post Review"}
      </Button>
    </div>
  );
}

function StudioReviewList({ slug, canRespond }: { slug: string; canRespond?: boolean }) {
  const { data: reviews, isLoading } = useGetStudioReviewsQuery(slug);
  const averageRating = reviews && reviews.length > 0
    ? reviews.reduce((sum, r) => sum + r.rating, 0) / reviews.length
    : null;
  return (
    <ReviewList
      reviews={reviews} isLoading={isLoading} averageRating={averageRating} canRespond={canRespond}
    />
  );
}

function ArtistReviewList({ slug, canRespond }: { slug: string; canRespond?: boolean }) {
  const { data: reviews, isLoading } = useGetArtistReviewsQuery(slug);
  const averageRating = reviews && reviews.length > 0
    ? reviews.reduce((sum, r) => sum + r.rating, 0) / reviews.length
    : null;
  return (
    <ReviewList
      reviews={reviews} isLoading={isLoading} averageRating={averageRating} canRespond={canRespond}
    />
  );
}

function PortfolioImageReviewList({ imageId, canRespond }: { imageId: string; canRespond?: boolean }) {
  const { data: reviews, isLoading } = useGetPortfolioImageReviewsQuery(imageId);
  const averageRating = reviews && reviews.length > 0
    ? reviews.reduce((sum, r) => sum + r.rating, 0) / reviews.length
    : null;
  return (
    <ReviewList
      reviews={reviews} isLoading={isLoading} averageRating={averageRating} canRespond={canRespond}
    />
  );
}

function ReviewList({
  reviews,
  isLoading,
  averageRating,
  canRespond,
}: {
  reviews:       ReviewResponse[] | undefined;
  isLoading:     boolean;
  averageRating: number | null;
  canRespond?:   boolean;
}) {
  const [showAll, setShowAll] = useState(false);

  const visible = !reviews
    ? []
    : showAll
    ? reviews
    : reviews.slice(0, PAGE_SIZE);

  const hiddenCount = (reviews?.length ?? 0) - visible.length;

  return (
    <>
      {averageRating !== null && reviews && reviews.length > 0 && (
        <div className="flex items-center gap-2 pb-3">
          <StarRating value={Math.round(averageRating)} size="sm" />
          <span className="text-sm font-semibold tabular-nums">
            {averageRating.toFixed(1)}
          </span>
          <span className="text-xs text-muted-foreground">
            · {reviews.length} review{reviews.length !== 1 ? "s" : ""}
          </span>
        </div>
      )}
      {isLoading ? (
        <ReviewsSkeleton />
      ) : !reviews || reviews.length === 0 ? (
        <div className="py-4 flex flex-col items-center gap-2 text-center">
          <div className="flex gap-0.5 opacity-30">
            {[1,2,3,4,5].map((i) => (
              <Star key={i} className="h-4 w-4 text-amber-400" aria-hidden="true" />
            ))}
          </div>
          <p className="text-sm text-muted-foreground">No reviews yet.</p>
        </div>
      ) : (
        <div>
          {visible.map((r) => (
            <ReviewCard key={r.id} review={r} canRespond={canRespond} />
          ))}

          {!showAll && hiddenCount > 0 && (
            <button
              type="button"
              onClick={() => setShowAll(true)}
              className="mt-3 w-full py-2.5 text-xs text-muted-foreground
                         hover:text-foreground border border-border/40
                         rounded-md transition-colors"
            >
              Show {hiddenCount} more review{hiddenCount !== 1 ? "s" : ""}
            </button>
          )}
        </div>
      )}
    </>
  );
}

interface Props {
  slug:        string;
  target:      "studio" | "artist" | "tattoo";
  token:       string | null;
  imageId?:    string;
  canRespond?: boolean;
}

export function ReviewSection({ slug, target, token, imageId, canRespond }: Props) {
  return (
    <section className="space-y-4" aria-labelledby="reviews-heading">
      <div className="flex items-center gap-2">
        <MessageSquare className="h-4 w-4 text-muted-foreground/70" aria-hidden="true" />
        <h2 id="reviews-heading" className="text-base font-semibold">Reviews</h2>
      </div>

      {target === "studio"
        ? <StudioReviewList   slug={slug} canRespond={canRespond} />
        : target === "artist"
        ? <ArtistReviewList   slug={slug} canRespond={canRespond} />
        : <PortfolioImageReviewList imageId={imageId ?? ""} canRespond={canRespond} />}

      <div className="pt-2 border-t border-border/40">
        <ReviewForm slug={slug} token={token} target={target} imageId={imageId} />
      </div>
    </section>
  );
}
