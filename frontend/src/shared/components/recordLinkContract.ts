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

// The record last opened from a list, in memory only: going back within the app never reloads the page, so
// memory is enough, and nothing about which record was opened (or the list's search) is written to browser
// storage. A full reload simply loses the focus restore.
let returnTarget: { list: string; to: string } | null = null;

/** A record link was followed from the list at `list`. */
export function rememberReturnTarget(list: string, to: string): void {
  returnTarget = { list, to };
}

/** True, once, when `to` is the record last opened from `list`: that link takes focus on the way back. */
export function takeReturnTarget(list: string, to: string): boolean {
  if (returnTarget === null || returnTarget.list !== list || returnTarget.to !== to) return false;
  returnTarget = null;
  return true;
}

/** Test-only: forget the remembered record between tests. */
export function resetReturnTarget(): void {
  returnTarget = null;
}
