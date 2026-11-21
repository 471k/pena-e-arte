import { useAppSelector } from "@/app/hooks";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";
import { ROLE_LABELS } from "@/shared/constants/roleLabels";

/**
 * Which studio this signed-in screen acts in (S08). Before slice 6a no layout showed it, so a person working at
 * two studios, or an admin in a support session, had nothing on screen naming the studio.
 *
 * - `header`: the studio's name beside the wordmark, from `md` up (the role is already beside the person's name
 *   in the user menu chip). It never shrinks below 6rem: at 640px a dual-role owner's header (with the
 *   Owner/Artist switch) squeezed it to zero width, divider and all, which the real-browser test caught. Below
 *   `md` the header has no room, so:
 * - `drawer`: the studio's name and the role at the top of the navigation drawer, which is the menu below `lg`.
 *
 * The name comes from `studios/me`, the read the owner and artist layouts already make for money formatting and
 * client pages already make for the studio's clock, so this adds no request there. In a support session the
 * banner's own studio name is used until that read lands, and the support MODE stays the banner's to say:
 * this label never repeats "Viewing as" or "Read-only".
 *
 * While loading it shows a placeholder bar; if the read fails it shows nothing rather than a guess.
 */
export function StudioContextLabel({ variant }: { variant: "header" | "drawer" }) {
  const tenantId      = useAppSelector((s) => s.auth.tenantId);
  const role          = useAppSelector((s) => s.auth.role);
  const impersonation = useAppSelector((s) => s.auth.impersonation);
  const { data: studio, isLoading } = useGetMyStudioQuery(undefined, { skip: !tenantId });

  if (!tenantId) return null;
  const name = studio?.name ?? impersonation?.studioName ?? null;

  if (variant === "header") {
    if (!name) {
      return isLoading
        ? <span className="hidden md:block h-3.5 w-24 rounded bg-muted animate-pulse" aria-hidden="true" />
        : null;
    }
    return (
      <span className="hidden md:flex items-center gap-2 min-w-0 text-sm text-muted-foreground">
        <span className="h-4 w-px bg-border shrink-0" aria-hidden="true" />
        <span className="sr-only">Studio: </span>
        <span className="truncate min-w-[6rem] max-w-[14rem] font-medium text-foreground" title={name}>{name}</span>
      </span>
    );
  }

  if (!name) return null;
  return (
    <span className="block text-sm text-muted-foreground">
      <span className="sr-only">Studio: </span>
      <span className="font-medium text-foreground break-words">{name}</span>
      {role && <> · {ROLE_LABELS[role]}</>}
    </span>
  );
}
