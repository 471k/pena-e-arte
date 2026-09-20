import { Button } from "@/shared/components/ui/button";
import { Alert } from "@/shared/components/ui/alert";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  useGetSocialLinksQuery,
  type SocialSubjectType,
  type SocialPlatform,
} from "../socialApi";
import { SocialLinkRows } from "./SocialLinkRows";

interface SocialLinksCardProps {
  subjectType: SocialSubjectType;
  subjectId:   string;
  /** Which platforms this card manages. Defaults to all five — pass a subset to
   * exclude one already managed elsewhere. */
  platforms?: readonly SocialPlatform[];
  /** Whether the viewer may connect/verify/edit/disconnect. The studio subject's endpoints are
   * OwnerOnly, so Studio Settings (already owner-gated at the route) leaves this at its default
   * of true. The artist subject's endpoints are ArtistAndAbove with a handler-side ownership
   * guard — the artist page decides per viewer (see ArtistSocialTab). false renders every row
   * read-only. */
  canManage?: boolean;
}

const DEFAULT_PLATFORMS: readonly SocialPlatform[] = ["Instagram", "TikTok", "Facebook", "X", "YouTube"];

/**
 * A self-contained list of connection rows for one subject (used by Studio Settings). The artist
 * profile's Social tab composes SocialLinkRows itself so it can put Instagram's photo-sync row in
 * the same list under one loading/error state.
 */
export function SocialLinksCard({
  subjectType, subjectId, platforms = DEFAULT_PLATFORMS, canManage = true,
}: SocialLinksCardProps) {
  const { data: links = [], isLoading, isError, refetch } = useGetSocialLinksQuery({ subjectType, subjectId });

  if (isLoading) {
    return (
      <div className="space-y-3" aria-busy="true">
        {platforms.map((p) => <Skeleton key={p} className="h-[74px] w-full" />)}
      </div>
    );
  }

  if (isError) {
    return (
      <Alert variant="destructive" className="flex items-center justify-between gap-3">
        <span>We couldn't load the connected accounts.</span>
        <Button size="sm" variant="outline" onClick={() => void refetch()}>Try again</Button>
      </Alert>
    );
  }

  return (
    <ul aria-label="Connected accounts" className="space-y-3">
      <SocialLinkRows
        subjectType={subjectType}
        subjectId={subjectId}
        platforms={platforms}
        links={links}
        canManage={canManage}
      />
    </ul>
  );
}
