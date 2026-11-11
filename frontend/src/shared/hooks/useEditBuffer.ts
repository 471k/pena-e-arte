import { useCallback, useState, type Dispatch, type SetStateAction } from "react";

/**
 * A form's edit buffer, filled from server data once per thing being edited, never again on a refetch.
 *
 * Why not `if (data !== synced) { setSynced(data); setForm(toForm(data)); }`, which five forms used:
 * RTK Query returns a new object on every refetch, even when nothing changed, so that guard replaced
 * whatever the user was typing each time the query refetched (the save's own invalidation, a tag
 * invalidated by another card on the page, a remount). The user saw their edit revert (A07a cleanup,
 * finding 2).
 *
 * - `source`: the data for the thing being edited. Pass RTK Query's `currentData`, not `data`: `data`
 *   still holds the previous argument's result while a new one loads, so it could fill the form for
 *   the wrong artist.
 * - `key`: what is being edited (an artist id, a studio id, an opened sheet). The buffer refills when
 *   the key changes. Pass `undefined` while there is nothing to edit (closed, not loaded yet); the next
 *   key then fills it afresh, even if it is the same key as before.
 * - After a successful save, call `reset` with what the server returned when it may differ from what
 *   was typed; otherwise the buffer already holds what was saved and nothing needs doing.
 *
 * The fill runs during render, not in an effect (React's "adjusting state when a prop changes"), so the
 * first paint already shows the loaded values.
 */
export function useEditBuffer<TData, TForm>(
  source: TData | undefined,
  key: string | undefined,
  toForm: (data: TData) => TForm,
): [TForm | null, Dispatch<SetStateAction<TForm | null>>, (data: TData) => void] {
  const [form, setForm] = useState<TForm | null>(null);
  const [filledFor, setFilledFor] = useState<string | undefined>(undefined);

  if (key === undefined) {
    if (filledFor !== undefined) setFilledFor(undefined);
  } else if (source !== undefined && filledFor !== key) {
    setFilledFor(key);
    setForm(toForm(source));
  }

  const reset = useCallback((data: TData) => setForm(toForm(data)), [toForm]);

  return [form, setForm, reset];
}
