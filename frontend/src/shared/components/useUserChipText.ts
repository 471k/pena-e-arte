import { useAppSelector } from "@/app/hooks";
import { ROLE_LABELS } from "@/shared/constants/roleLabels";

/** The name and role the user chip shows, or null while the user is still loading. UserMenu builds its accessible
 *  name from the same two values, so the words a voice-control user sees are the words in the name (WCAG 2.5.3). */
export function useUserChipText(): { displayName: string; roleLabel: string } | null {
  const user = useAppSelector((s) => s.auth.user);
  const role = useAppSelector((s) => s.auth.role);
  if (!user || !role) return null;
  return { displayName: user.name ?? user.email.split("@")[0], roleLabel: ROLE_LABELS[role] };
}
