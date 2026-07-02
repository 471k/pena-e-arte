import { AtSign, Eye, EyeOff, Unlink, ExternalLink } from "lucide-react";
import { Button }  from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import { Badge } from "@/shared/components/ui/badge";
import { cn } from "@/shared/utils/cn";
import { toast } from "sonner";
import {
  useGetInstagramStatusQuery,
  useGetInstagramPostsQuery,
  useLazyGetInstagramConnectUrlQuery,
  useToggleInstagramPostVisibilityMutation,
  useDisconnectInstagramMutation,
} from "../artistsApi";

function formatSyncedAt(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit",
  });
}

interface InstagramTabProps {
  artistId: string;
  /** Owner-only: connect/disconnect the Instagram account (matches OwnerOnly policy). */
  canConnect: boolean;
  /** Owner or the artist's own profile: toggle per-post visibility (matches ArtistAndAbove policy). */
  canManagePosts: boolean;
}

export function InstagramTab({ artistId, canConnect, canManagePosts }: InstagramTabProps) {
  const { data: status, isLoading: statusLoading } = useGetInstagramStatusQuery(artistId);

  const { data: posts = [], isLoading: postsLoading } =
    useGetInstagramPostsQuery({ artistId }, { skip: !status?.isConnected });

  const [fetchConnectUrl] = useLazyGetInstagramConnectUrlQuery();
  const [toggleVisibility] = useToggleInstagramPostVisibilityMutation();
  const [disconnect] = useDisconnectInstagramMutation();

  async function handleConnect() {
    const result = await fetchConnectUrl(artistId);
    if ("data" in result && result.data) {
      window.open(result.data.authUrl, "_blank", "noopener,noreferrer");
    } else {
      toast.error("Failed to start Instagram connection.");
    }
  }

  async function handleDisconnect() {
    if (!window.confirm("Disconnect Instagram? Synced posts remain but no new posts will be fetched."))
      return;
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
    return (
      <div className="space-y-3">
        {[1, 2, 3].map((i) => <Skeleton key={i} className="h-14 w-full" />)}
      </div>
    );
  }

  if (!status?.isConnected) {
    return (
      <div className="flex flex-col items-center gap-4 py-12 text-center">
        <AtSign className="h-10 w-10 text-muted-foreground" aria-hidden="true" />
        <p className="text-sm text-muted-foreground max-w-xs">
          Connect this artist's Instagram account to automatically sync their posts
          to their public portfolio.
        </p>
        {canConnect && (
          <Button onClick={handleConnect} className="gap-2">
            <AtSign className="h-4 w-4" aria-hidden="true" />
            Connect Instagram
            <ExternalLink className="h-3 w-3" aria-hidden="true" />
          </Button>
        )}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      <Card>
        <CardContent className="p-4 flex items-center justify-between gap-3 flex-wrap">
          <div className="flex items-center gap-3">
            <AtSign className="h-5 w-5 text-pink-500" aria-hidden="true" />
            <div>
              <p className="text-sm font-medium">@{status.username}</p>
              {status.lastSyncedAt && (
                <p className="text-xs text-muted-foreground">
                  Last synced {formatSyncedAt(status.lastSyncedAt)}
                </p>
              )}
            </div>
            <Badge variant="secondary">{status.postCount} posts</Badge>
          </div>

          {canConnect && (
            <Button
              variant="ghost"
              size="sm"
              onClick={handleDisconnect}
              className="gap-1.5 text-destructive hover:text-destructive"
            >
              <Unlink className="h-3.5 w-3.5" aria-hidden="true" />
              Disconnect
            </Button>
          )}
        </CardContent>
      </Card>

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
    </div>
  );
}
