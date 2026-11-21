import type { Role } from "@/shared/types/roles";

/** How a role is named on screen (the user menu chip, the studio context in the phone drawer). */
export const ROLE_LABELS: Record<Role, string> = {
  client:  "Client",
  artist:  "Artist",
  owner:   "Owner",
  admin:   "Admin",
};
