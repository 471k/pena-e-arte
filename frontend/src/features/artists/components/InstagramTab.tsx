import { useState } from "react";
import { Eye, EyeOff } from "lucide-react";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { cn } from "@/shared/utils/cn";
import { toast } from "sonner";
import {
  useGetInstagramStatusQuery,
  useGetInstagramPostsQuery,
  useLazyGetInstagramConnectUrlQuery,
  useToggleInstagramPostVisibilityMutation,
  useDisconnectInstagramMutation,
} from "../artistsApi";
import { useGetSocialLinksQuery } from "@/features/social/socialApi";
import { ConnectionRow, type ConnectionRowAction } from "@/features/social/components/ConnectionRow";
import { ConfirmDisconnectDialog } from "@/features/social/components/ConfirmDisconnectDialog";
import { InstagramIcon } from "@/shared/components/icons/brand";

function formatSyncedAt(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit",
  });
}

interface InstagramTabProps {
  artistId: string;
  /** Owner, or the artist managing their own profile: connect/disconnect the Instagram account
   * (matches the ArtistAndAbove policy plus a handler-side ownership guard). false = read-only row. */
  canConnect: boolean;
  /** Owner or the artist's own profile: toggle per-post visibility (matches ArtistAndAbove policy). */
  canManagePosts: boolean;
  /** Viewer is looking at their own profile: second-person copy. */
  isSelf?: boolean;
  /** Used in third-person copy when an owner views someone else's profile. */
  firstName?: string;
}

/**
 * The Instagram <li> for the artist's Social tab: a ConnectionRow plus, when connected, the synced
 * photos panel (visibility toggles) directly under it. Renders inside ArtistSocialTab's <ul>, which
 * owns the section loading/error states — this component reads the same cached queries.
 */
export function InstagramTab({
  artistId, canConnect, canManagePosts, isSelf = false, firstName = "the artist",
}: InstagramTabProps) {
  const { data: status, isLoading: statusLoading } = useGetInstagramStatusQuery(artistId);

  const { data: socialLinks = [] } = useGetSocialLinksQuery(
    { subjectType: "Artist", subjectId: artistId },
    { skip: !status?.isConnected },
  );
  const isInstagramVerified =
    socialLinks.find((l) => l.platform === "Instagram")?.isVerified ?? false;

  const { data: posts = [], isLoading: postsLoading } =
    useGetInstagramPostsQuery({ artistId }, { skip: !status?.isConnected });

  const [fetchConnectUrl] = useLazyGetInstagramConnectUrlQuery();
  const [toggleVisibility] = useToggleInstagramPostVisibilityMutation();
  const [disconnect] = useDisconnectInstagramMutation();

  const [connecting, setConnecting] = useState(false);
  const [confirmOpen, setConfirmOpen] = useState(false);

  async function handleConnect() {
    // Open the tab synchronously, in direct response to the click, so browsers
    // (Firefox especially) still treat it as a trusted user gesture — opening it
    // only after the awaited fetch below resolves gets silently popup-blocked.
    const popup = window.open("about:blank", "_blank");
    setConnecting(true);

    const result = await fetchConnectUrl(artistId);
    setConnecting(false);
    if ("data" in result && result.data) {
      if (popup) {
        popup.location.href = result.data.authUrl;
      } else {
        toast.error("Pop-up blocked. Allow pop-ups for this site and try again.");
      }
    } else {
      popup?.close();
      toast.error("Couldn't start the Instagram connection. Try again.");
    }
  }

  async function handleDisconnect() {
    setConfirmOpen(false);
    const result = await disconnect(artistId);
    if ("error" in result) {
      toast.error("Failed to disconnect Instagram.");
    } else {
      toast.success("Instagram disconnected.");
    }
  }

  async function handleToggleVisibility(postId: string, isVisible: boolean) {
    const result = await toggleVisibility({ artistId, postId, isVisible });
    if ("error" in result) {
      toast.error("Failed to update post visibility.");
    }
  }

  if (statusLoading) {
    return <li><Skeleton className="h-[74px] w-full" /></li>;
  }

  const isConnected = status?.isConnected === true;

  let action: ConnectionRowAction | null = null;
  if (canConnect) {
    action = isConnected
      ? { kind: "disconnect", onClick: () => setConfirmOpen(true) }
      : { kind: "connect", onClick: () => void handleConnect(), busy: connecting };
  }

  const helper = isSelf
    ? "Connect Instagram to automatically show your latest posts on your public portfolio."
    : `Connect ${firstName}'s Instagram to automatically show their latest posts on their public portfolio.`;

  const detail = isConnected && status
    ? [
        status.lastSyncedAt ? `Last synced ${formatSyncedAt(status.lastSyncedAt)}` : null,
        `${status.postCount} ${status.postCount === 1 ? "post" : "posts"}`,
      ].filter((part): part is string => part !== null).join(" · ")
    : undefined;

  return (
    <ConnectionRow
      icon={InstagramIcon}
      label="Instagram"
      handle={isConnected ? (status?.username ?? null) : null}
      isVerified={isConnected && isInstagramVerified}
      action={action}
      helper={canConnect ? helper : undefined}
      detail={detail}
    >
      {isConnected && (
        <section aria-label="Synced Instagram posts" className="space-y-2">
          {postsLoading && (
            <div className="grid grid-cols-3 gap-2">
              {Array.from({ length: 9 }).map((_, i) => (
                <Skeleton key={i} className="aspect-square w-full rounded-md" />
              ))}
            </div>
          )}

          {!postsLoading && posts.length === 0 && (
            <p className="text-sm text-muted-foreground text-center py-8">
              No posts synced yet. The nightly job will run automatically.
            </p>
          )}

          {!postsLoading && posts.length > 0 && (
            <div className="grid grid-cols-3 gap-2">
              {posts.map((post) => {
                const imgSrc = post.mediaUrl ?? post.thumbnailUrl ?? "";
                return (
                  <div key={post.id} className="relative group">
                    <img
                      src={imgSrc}
                      alt={post.caption?.slice(0, 80) ?? "Instagram post"}
                      className={cn(
                        "aspect-square w-full object-cover rounded-md transition-opacity",
                        !post.isVisible && "opacity-40",
                      )}
                      loading="lazy"
                    />
                    {canManagePosts && (
                      <button
                        type="button"
                        aria-label={post.isVisible ? "Hide from portfolio" : "Show in portfolio"}
                        onClick={() => void handleToggleVisibility(post.id, !post.isVisible)}
                        className="absolute top-1.5 right-1.5 rounded-md bg-background/80 p-1
                                   opacity-0 group-hover:opacity-100 transition-opacity
                                   focus-visible:opacity-100 focus-visible:ring-2 focus-visible:ring-ring"
                      >
                        {post.isVisible
                          ? <Eye className="h-3.5 w-3.5" aria-hidden="true" />
                          : <EyeOff className="h-3.5 w-3.5 text-muted-foreground" aria-hidden="true" />}
                      </button>
                    )}
                  </div>
                );
              })}
            </div>
          )}
        </section>
      )}

      <ConfirmDisconnectDialog
        platform="Instagram"
        body={
          isSelf
            ? "Your synced posts stay on your portfolio, but no new posts will be fetched."
            : `Synced posts stay on ${firstName}'s portfolio, but no new posts will be fetched.`
        }
        open={confirmOpen}
        onOpenChange={setConfirmOpen}
        onConfirm={() => void handleDisconnect()}
      />
    </ConnectionRow>
  );
}
