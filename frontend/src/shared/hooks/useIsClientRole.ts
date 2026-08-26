import { useAppSelector } from "@/app/hooks";
import { Role } from "@/shared/types/roles";

/// Filing a conduct report requires ClientOnly server-side — `usePermission`'s rank model
/// ("at least this role") can't express an exact match, so this checks role equality directly
/// and requires an authenticated token, matching the backend policy it mirrors.
export function useIsClientRole(): boolean {
  const token = useAppSelector((s) => s.auth.token);
  const role = useAppSelector((s) => s.auth.role);
  return !!token && role === Role.Client;
}
