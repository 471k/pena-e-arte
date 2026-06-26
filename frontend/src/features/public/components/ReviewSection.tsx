import { useState, useEffect } from "react";
import { Link } from "react-router-dom";
import { MessageSquare, CheckCircle } from "lucide-react";
import { Button }   from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { StarRating } from "@/shared/components/ui/StarRating";
import {
  useGetStudioReviewsQuery,
  useGetArtistReviewsQuery,
  useCreateStudioReviewMutation,
  useCreateArtistReviewMutation,
  type ReviewResponse,
} from "../publicApi";

function ReviewCard({ review }: { review: ReviewResponse }) {
  return (
    <div className="py-4 border-b last:border-b-0 space-y-1.5">
      <div className="flex items-center justify-between gap-2 flex-wrap">
        <span className="text-sm font-medium">{review.authorName}</span>
        <div className="flex items-center gap-2">
          <StarRating value={review.rating} />
          <span className="text-xs text-muted-foreground">
            {new Date(review.createdAt).toLocaleDateString("en-GB", {
              day: "numeric", month: "short", year: "numeric",
            })}
          </span>
        </div>
      </div>
      <p className="text-sm text-muted-foreground whitespace-pre-wrap">{review.body}</p>
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
  slug:   string;
  token:  string | null;
  target: "studio" | "artist";
}

function ReviewForm({ slug, token, target }: ReviewFormProps) {
  const [rating,  setRating]  = useState(0);
  const [body,    setBody]    = useState("");
  const [error,   setError]   = useState<string | null>(null);
  const [success, setSuccess] = useState(false);

  const [createStudioReview, { isLoading: isStudioSubmitting }] = useCreateStudioReviewMutation();
  const [createArtistReview, { isLoading: isArtistSubmitting }] = useCreateArtistReviewMutation();

  const isSubmitting = target === "studio" ? isStudioSubmitting : isArtistSubmitting;

  useEffect(() => {
    if (!success) return;
    const id = window.setTimeout(() => setSuccess(false), 4000);
    return () => window.clearTimeout(id);
  }, [success]);

  function handleSubmit() {
    if (rating === 0) { setError("Please select a star rating."); return; }
    if (body.trim().length < 10) { setError("Review must be at least 10 characters."); return; }

    setError(null);
    const mutation = target === "studio" ? createStudioReview : createArtistReview;
    mutation({ slug, rating, body: body.trim() })
      .unwrap()
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
    const returnUrl = target === "studio" ? `/s/${slug}` : `/artist/${slug}`;
    return (
      <div
        className="rounded-lg border bg-muted/20 px-5 py-6
                   flex flex-col items-center gap-3 text-center"
      >
        <p className="text-sm text-muted-foreground">
          Sign in to share your experience with this {target}.
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

      <StarRating
        value={rating}
        interactive
        onChange={(r) => { setRating(r); setError(null); }}
      />

      <textarea
        id="review-body"
        aria-label="Write a review"
        className="w-full min-h-[80px] resize-none rounded-md border bg-background px-3 py-2 text-sm
                   focus:outline-none focus:ring-1 focus:ring-ring placeholder:text-muted-foreground"
        placeholder="Share your experience…"
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
        disabled={isSubmitting}
        aria-label="Submit review"
      >
        {isSubmitting ? "Submitting…" : "Submit review"}
      </Button>
    </div>
  );
}

function StudioReviewList({ slug }: { slug: string }) {
  const { data: reviews, isLoading } = useGetStudioReviewsQuery(slug);
  const averageRating = reviews && reviews.length > 0
    ? reviews.reduce((sum, r) => sum + r.rating, 0) / reviews.length
    : null;
  return <ReviewList reviews={reviews} isLoading={isLoading} averageRating={averageRating} />;
}

function ArtistReviewList({ slug }: { slug: string }) {
  const { data: reviews, isLoading } = useGetArtistReviewsQuery(slug);
  const averageRating = reviews && reviews.length > 0
    ? reviews.reduce((sum, r) => sum + r.rating, 0) / reviews.length
    : null;
  return <ReviewList reviews={reviews} isLoading={isLoading} averageRating={averageRating} />;
}

function ReviewList({
  reviews,
  isLoading,
  averageRating,
}: {
  reviews:       ReviewResponse[] | undefined;
  isLoading:     boolean;
  averageRating: number | null;
}) {
  return (
    <>
      {averageRating !== null && reviews && (
        <p className="text-xs text-muted-foreground">
          {averageRating.toFixed(1)} / 5 · {reviews.length} review{reviews.length !== 1 ? "s" : ""}
        </p>
      )}
      {isLoading ? (
        <ReviewsSkeleton />
      ) : !reviews || reviews.length === 0 ? (
        <p className="text-sm text-muted-foreground py-4">
          No reviews yet. Be the first to leave one.
        </p>
      ) : (
        <div>
          {reviews.map((r) => (
            <ReviewCard key={r.id} review={r} />
          ))}
        </div>
      )}
    </>
  );
}

interface Props {
  slug:   string;
  target: "studio" | "artist";
  token:  string | null;
}

export function ReviewSection({ slug, target, token }: Props) {
  return (
    <section className="space-y-5" aria-labelledby="reviews-heading">
      <div className="flex items-center gap-2">
        <MessageSquare className="h-5 w-5 text-muted-foreground/70" aria-hidden="true" />
        <h2 id="reviews-heading" className="text-lg font-semibold">Reviews</h2>
      </div>

      <ReviewForm slug={slug} token={token} target={target} />

      {target === "studio"
        ? <StudioReviewList slug={slug} />
        : <ArtistReviewList slug={slug} />}
    </section>
  );
}
