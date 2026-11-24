import { cn } from "@/shared/utils/cn";

/**
 * A person's initials drawn as a picture: the stand-in for their photo when there isn't one.
 *
 * The letters are CSS-generated content, not a text node, on purpose. An avatar sits inside a
 * control whose name is the person ("Rafaela Costa") or the menu ("Gabriel, Owner, user menu"),
 * and WCAG 2.5.3 Label in Name asks that the visible label be part of that name, so a voice-control
 * user can say what they see. Initials as text made "RC Rafaela Costa" the visible label, which no
 * name should repeat and nobody says aloud. The photo it replaces is an `alt=""` image with no text
 * at all, so drawing the initials gives the fallback the same standing: decorative, not the label.
 * Screen readers already skip it (aria-hidden); this only stops it being counted as label text.
 */
export function InitialsGlyph({ initials, className }: { initials: string; className?: string }) {
  return (
    <span
      aria-hidden="true"
      data-initials={initials}
      className={cn("before:content-[attr(data-initials)]", className)}
    />
  );
}
