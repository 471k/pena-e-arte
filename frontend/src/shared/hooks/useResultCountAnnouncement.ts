import { useState } from "react";

/**
 * What a list page's LiveAnnouncement should say after the person changes its search or filters (S06): how many
 * results there are now. A list used to announce only its load errors, so a screen-reader user who typed a search
 * heard nothing and had to go looking for whether anything matched.
 *
 * The rule (see LiveAnnouncement): only a change the PERSON caused speaks. So nothing is said on the first load,
 * or when the data refreshes on its own (a SignalR push, a refetch on focus) with the same query, only after
 * `queryKey` has changed at least once. The message is empty while the new results are loading, so the same count
 * twice in a row is read twice.
 *
 * `queryKey` must cover every input the person controls (search text, toggles, filters) and nothing else.
 */
export function useResultCountAnnouncement({
  queryKey,
  count,
  settled,
  noun,
  hasMore = false,
}: {
  queryKey: string;
  count: number;
  /** False while the results for the current query are still loading, or when the load failed. */
  settled: boolean;
  noun: { one: string; many: string };
  /** The list is paged and more can be loaded: the count is how many are shown, not a total. */
  hasMore?: boolean;
}): string {
  const [lastKey, setLastKey]   = useState(queryKey);
  const [touched, setTouched]   = useState(false);
  // Adjusting state while rendering when an input changes (react.dev, "storing information from previous
  // renders") rather than in an effect, so the message is right in the same render the new key arrives in.
  if (queryKey !== lastKey) {
    setLastKey(queryKey);
    setTouched(true);
  }

  if (!touched || !settled) return "";
  if (count === 0) return `No ${noun.many} found.`;
  const shown = `${count} ${count === 1 ? noun.one : noun.many}`;
  return hasMore ? `${shown} shown. More can be loaded.` : `${shown} found.`;
}
