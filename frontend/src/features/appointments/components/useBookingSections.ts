import { useState } from "react";

/** The id of a booking section's heading: the focus target for Change and Back to review (C02). */
export function bookingSectionHeadingId(sectionId: string): string {
  return `${sectionId}-heading`;
}

function focusSection(sectionId: string): void {
  const heading = document.getElementById(bookingSectionHeadingId(sectionId));
  if (!heading) return;
  const reduceMotion = typeof window.matchMedia === "function"
    && window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  // jsdom has no scrollIntoView; a real browser always does.
  if (typeof heading.scrollIntoView === "function") {
    heading.scrollIntoView({ block: "start", behavior: reduceMotion ? "auto" : "smooth" });
  }
  heading.focus({ preventScroll: true });
}

/**
 * Change from the review moves focus to that section's heading and offers a way straight back; Back to review
 * returns focus to the review. Only the section that was opened from the review shows the way back.
 */
export function useBookingSections(reviewId: string) {
  const [changing, setChanging] = useState<string | null>(null);
  return {
    changing,
    change(sectionId: string) {
      setChanging(sectionId);
      focusSection(sectionId);
    },
    backToReview() {
      setChanging(null);
      focusSection(reviewId);
    },
  };
}
