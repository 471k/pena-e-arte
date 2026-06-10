import { useAppSelector } from "@/app/hooks";
import type { User } from "@/shared/types/roles";

export function useCurrentUser(): User | null {
  return useAppSelector((s) => s.auth.user);
}
