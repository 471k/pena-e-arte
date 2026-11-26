import { InitialsGlyph } from "./InitialsGlyph";
import { useUserChipText } from "./useUserChipText";

/** `compact`: avatar only at every width (the folded header, S05), not just below sm. */
export function UserChip({ compact = false }: { compact?: boolean }) {
  const text = useUserChipText();

  if (!text) {
    return (
      <div className="flex items-center gap-2.5">
        <div className="h-7 w-7 rounded-full bg-muted animate-pulse shrink-0" />
        <div className={compact ? "hidden" : "hidden sm:flex flex-col gap-1"}>
          <div className="h-3 w-16 rounded bg-muted animate-pulse" />
          <div className="h-2.5 w-12 rounded bg-muted animate-pulse" />
        </div>
      </div>
    );
  }

  const { displayName, roleLabel } = text;

  return (
    <div className="flex items-center gap-2.5">
      <InitialsGlyph
        initials={displayName[0].toUpperCase()}
        className="h-7 w-7 rounded-full bg-primary text-primary-foreground flex items-center justify-center text-xs font-semibold shrink-0"
      />
      {/* Name and role drop out below sm: on a phone the avatar alone keeps the header inside the viewport
          (the button's aria-label still names the person and the menu). */}
      <div className={compact ? "hidden" : "hidden sm:flex flex-col leading-none gap-1"}>
        {/* The space is a real text node: a flex column never renders it, but without it the text reads
            "GabrielAdmin" as one word to anything that reads text, axe's Label in Name check included. */}
        <span className="text-sm font-medium text-foreground">{displayName}</span>{" "}
        <span className="text-xs text-muted-foreground">{roleLabel}</span>
      </div>
    </div>
  );
}
