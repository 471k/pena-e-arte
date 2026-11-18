import { cn } from "@/shared/utils/cn";

interface WordmarkProps {
  /** Size, visibility and spacing only (e.g. "text-2xl", "max-[359px]:hidden"). The family and
   *  weight belong to the wordmark and are not overridable here. */
  className?: string;
}

/**
 * The TattooOS wordmark: Bodoni Moda 700 (docs/03, "wordmark only").
 *
 * This is the ONLY consumer of the `font-wordmark` utility / `--font-wordmark` token, which is
 * how "Bodoni is for the wordmark only" holds by construction rather than by discipline:
 * a second use is a visible, greppable change, and wordmarkFont.test.ts fails on it. Use this
 * component wherever the product name is shown as a brand mark (headers, auth cards). Plain
 * prose that merely mentions TattooOS (the footer's copyright line, Help text) is not the
 * wordmark and stays in the body face.
 */
export function Wordmark({ className }: WordmarkProps) {
  return <span className={cn(className, "font-wordmark font-bold")}>TattooOS</span>;
}
