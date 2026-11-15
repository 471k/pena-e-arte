// The non-component half of RecordLink's contract (S03): classes for a card and its actions around a
// stretched record link, and the check a table row runs before opening its record on a click.

export const RECORD_CONTAINER =
  "relative has-[[data-record-link]:focus-visible]:ring-2 has-[[data-record-link]:focus-visible]:ring-ring";

/**
 * Sibling actions on a card with a stretched {@link RecordLink}: lifted above the link's overlay. Pair it with
 * a `data-record-actions` attribute so a click on the area's plain text ("Delete Ana?") doesn't open the record.
 */
export const RECORD_ACTIONS = "relative z-10";

/**
 * True when a click must not open the row's record: it landed on (or inside) one of the row's own controls,
 * or outside the row's DOM altogether — a row's dropdown menu or dialog renders in a portal, and React still
 * bubbles that click up to the row. Used by table rows, which open their record for a pointer while the
 * title link stays the keyboard path.
 */
export function isInsideControl(target: EventTarget | null, container: Element): boolean {
  if (!(target instanceof Element)) return false;
  if (!container.contains(target)) return true;
  const control = target.closest(
    "a, button, input, select, textarea, label, summary, [role='button'], [role='menuitem'], [role='checkbox'], [role='switch'], [role='dialog'], [data-record-actions]",
  );
  return control !== null && container.contains(control);
}
