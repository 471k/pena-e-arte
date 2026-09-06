import { useAppSelector } from "@/app/hooks";
import { Role } from "@/shared/types/roles";

const RANK: Record<Role, number> = {
  [Role.Client]: 0,
  [Role.Artist]: 1,
  [Role.Owner]:  2,
  [Role.Admin]: 3,
};

export function hasPermission(role: Role | null, requiredRole: Role): boolean {
  if (!role) return false;
  return RANK[role] >= RANK[requiredRole];
}

export function usePermission(requiredRole: Role): boolean {
  const role = useAppSelector((s) => s.auth.role);
  return hasPermission(role, requiredRole);
}
