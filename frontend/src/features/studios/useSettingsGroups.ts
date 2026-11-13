import { useCallback, useState } from "react";
import { useLocation, useSearchParams } from "react-router-dom";
import {
  ALWAYS_SHOWN_GROUP,
  SETTINGS_SECTION_PARAM,
  isSettingsGroupId,
  type SettingsGroupId,
} from "./settingsGroups";

export interface SettingsGroupsController {
  isOpen: (id: SettingsGroupId) => boolean;
  toggle: (id: SettingsGroupId) => void;
  open:   (id: SettingsGroupId) => void;
  /**
   * The group the latest navigation named, with that navigation's key, for StudioSettingsSections to
   * scroll to once it is on screen (the page shows a skeleton first, so the hook's own render is too early).
   */
  scrollTarget: { id: SettingsGroupId; navigationKey: string } | null;
}

/**
 * Which Studio Settings groups are open (E05c, O04). Not remembered between visits: each visit starts with
 * every collapsible group closed, or with the group a link asked for (`?section=`) open. Business details is
 * always shown (ALWAYS_SHOWN_GROUP). Every navigation that names a
 * section opens it, including one to the URL already showing (the tour's consecutive steps on this page
 * each navigate, and an owner may have collapsed the group in between), so a link never lands on a closed
 * group.
 */
export function useSettingsGroups(): SettingsGroupsController {
  const [params] = useSearchParams();
  const location = useLocation();
  const raw = params.get(SETTINGS_SECTION_PARAM);
  const section: SettingsGroupId | null = isSettingsGroupId(raw) ? raw : null;

  const [openIds, setOpenIds] = useState<ReadonlySet<SettingsGroupId>>(
    () => new Set(section ? [section] : []),
  );

  // Adjusting state when the navigation changes, during render (React's pattern for "reset state when a
  // prop changes"), so the group is open in the same commit the link lands, before anything measures it.
  const [seenNavigation, setSeenNavigation] = useState(location.key);
  if (seenNavigation !== location.key) {
    setSeenNavigation(location.key);
    if (section && !openIds.has(section)) setOpenIds(new Set(openIds).add(section));
  }

  const isOpen = useCallback((id: SettingsGroupId) => id === ALWAYS_SHOWN_GROUP || openIds.has(id), [openIds]);
  const toggle = useCallback((id: SettingsGroupId) => {
    setOpenIds((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }, []);
  const open = useCallback((id: SettingsGroupId) => {
    setOpenIds((prev) => (prev.has(id) ? prev : new Set(prev).add(id)));
  }, []);

  const scrollTarget = section ? { id: section, navigationKey: location.key } : null;

  return { isOpen, toggle, open, scrollTarget };
}
