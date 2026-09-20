import { ExternalLink } from "lucide-react";
import { Alert } from "@/shared/components/ui/alert";
import { Button } from "@/shared/components/ui/button";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { SocialLinkRows } from "@/features/social/components/SocialLinkRows";
import { useGetSocialLinksQuery } from "@/features/social/socialApi";
import { useGetInstagramStatusQuery } from "../artistsApi";
import { InstagramTab } from "./InstagramTab";

interface ArtistSocialTabProps {
  artistId: string;
  firstName: string;
  slug: string | null;
  /** Owner/admin: may act on any artist in the studio. */
  canManage: boolean;
  /** The viewer is looking at their own artist profile. */
  isOwnProfile: boolean;
}

const OTHER_PLATFORMS = ["TikTok", "Facebook", "X", "YouTube"] as const;

/**
 * The artist profile's Social tab: one list of connection rows (Instagram, whose photo sync has its
 * own panel, then the four verification-only platforms) under a single loading / error / read-only
 * treatment — a row is always either actionable or explained, never a dead-end button.
 */
export function ArtistSocialTab({ artistId, firstName, slug, canManage, isOwnProfile }: ArtistSocialTabProps) {
  const statusQuery = useGetInstagramStatusQuery(artistId);
  const linksQuery = useGetSocialLinksQuery({ subjectType: "Artist", subjectId: artistId });

  // canManageSocial is broader than canManage on purpose: it already reflects the target state
  // (owner OR the artist's own profile). Action buttons still gate on the narrower `canManage`
  // until the backend policies let an artist call these endpoints for their own profile —
  // showing a button here that 403s on click would be worse than the dead end this rebuild fixes.
  const canManageSocial = canManage || isOwnProfile;
  const canAct = canManage;

  const possessive = isOwnProfile ? "your" : `${firstName}'s`;

  if (statusQuery.isLoading || linksQuery.isLoading) {
    return (
      <div className="space-y-3" aria-busy="true" aria-label="Loading connected accounts">
        {Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-[74px] w-full" />)}
      </div>
    );
  }

  if (statusQuery.isError || linksQuery.isError) {
    return (
      <Alert variant="destructive" className="flex items-center justify-between gap-3">
        <span>We couldn't load {possessive} connected accounts.</span>
        <Button
          size="sm"
          variant="outline"
          onClick={() => {
            void statusQuery.refetch();
            void linksQuery.refetch();
          }}
        >
          Try again
        </Button>
      </Alert>
    );
  }

  const helper = canAct
    ? isOwnProfile
      ? "Link your accounts so clients can find you and see a Verified badge on your public profile."
      : `Link ${firstName}'s accounts so clients can find them and see a Verified badge on their public profile.`
    : canManageSocial
      ? "Your studio owner manages connections for your profile. Ask them to connect these accounts."
      : "Only the studio owner manages connections for this profile.";

  const publicProfileUrl =
    isOwnProfile && slug
      ? `${import.meta.env.VITE_PUBLIC_URL ?? window.location.origin}/artist/${slug}`
      : null;

  return (
    <section aria-labelledby="social-heading" className="space-y-3">
      <div className="space-y-1">
        <h2 id="social-heading" className="text-sm font-semibold">Connected accounts</h2>
        <p className="text-sm text-muted-foreground">{helper}</p>
      </div>

      <ul aria-label="Connected accounts" className="space-y-3">
        <InstagramTab
          artistId={artistId}
          canConnect={canAct}
          canManagePosts={canManageSocial}
          isSelf={isOwnProfile}
          firstName={firstName}
        />
        <SocialLinkRows
          subjectType="Artist"
          subjectId={artistId}
          platforms={OTHER_PLATFORMS}
          links={linksQuery.data ?? []}
          canManage={canAct}
          isSelf={isOwnProfile}
        />
      </ul>

      {publicProfileUrl && (
        <a
          href={publicProfileUrl}
          target="_blank"
          rel="noopener noreferrer"
          className="inline-flex items-center gap-1 text-sm text-muted-foreground hover:text-foreground"
        >
          View public profile
          <ExternalLink className="h-3 w-3" aria-hidden="true" />
        </a>
      )}
    </section>
  );
}
