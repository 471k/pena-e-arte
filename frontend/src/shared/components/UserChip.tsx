import { useAppSelector } from "@/app/hooks";
import type { Role } from "@/shared/types/roles";

const ROLE_LABELS: Record<Role, string> = {
  client:  "Client",
  artist:  "Artist",
  owner:   "Owner",
  issuer:  "Platform Admin",
};

export function UserChip() {
  const user = useAppSelector((s) => s.auth.user);
  const role = useAppSelector((s) => s.auth.role);

  if (!user || !role) return null;

  const displayName = user.name ?? user.email.split("@")[0];
  const initial     = displayName[0].toUpperCase();

  return (
    <div className="flex items-center gap-2.5">
      <div className="h-7 w-7 rounded-full bg-primary text-primary-foreground flex items-center justify-center text-xs font-semibold shrink-0">
        {initial}
      </div>
      <div className="flex flex-col leading-none gap-0.5">
        <span className="text-sm font-medium text-foreground">{displayName}</span>
        <span className="text-xs text-muted-foreground">{ROLE_LABELS[role]}</span>
      </div>
    </div>
  );
}
