import { useAppSelector } from "@/app/hooks";
import { Role } from "@/shared/types/roles";

const RANK: Record<Role, number> = {
  [Role.Client]: 0,
  [Role.Artist]: 1,
  [Role.Owner]:  2,
  [Role.Issuer]: 3,
};

export function usePermission(requiredRole: Role): boolean {
  const role = useAppSelector((s) => s.auth.role);
  if (!role) return false;
  return RANK[role] >= RANK[requiredRole];
}
