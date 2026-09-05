import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Input } from "@/shared/components/ui/input";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import { VerifiedSocialBadge } from "@/shared/components/VerifiedSocialBadge";
import { SOCIAL_PLATFORM_ICON, SOCIAL_PLATFORM_LABEL } from "@/shared/utils/socialPlatforms";
import { Unlink, ExternalLink, Copy } from "lucide-react";
import {
  useGetSocialLinksQuery,
  useLazyGetSocialConnectUrlQuery,
  useUpdateSocialHandleMutation,
  useRequestSocialVerificationCodeMutation,
  useVerifySocialBioCodeMutation,
  useDisconnectSocialAccountMutation,
  type SocialSubjectType,
  type SocialPlatform,
  type SocialLinkStatus,
} from "../socialApi";

interface SocialLinksCardProps {
  subjectType: SocialSubjectType;
  subjectId:   string;
  /** Which platforms this card manages. Defaults to all five — pass a subset to
   * exclude one already managed elsewhere (e.g. the artist detail page keeps
   * Instagram on its own dedicated InstagramTab for photo sync, and only shows the
   * other four platforms here). */
  platforms?: readonly SocialPlatform[];
  /** Owner-only, matching every backend endpoint this card calls (connect-url,
   * handle, request-code, verify-code, disconnect are all OwnerOnly). An artist
   * viewing their own profile can see this card (GetSocialLinksQuery is
   * ArtistAndAbove) but must get a read-only view, not buttons that always 403 —
   * mirrors InstagramTab.tsx's canConnect prop. Defaults to true for callers (like
   * Studio Settings) that are already Owner-gated at the route level. */
  canManage?: boolean;
}

const DEFAULT_PLATFORMS: readonly SocialPlatform[] = ["Instagram", "TikTok", "Facebook", "X", "YouTube"];

export function SocialLinksCard({
  subjectType, subjectId, platforms = DEFAULT_PLATFORMS, canManage = true,
}: SocialLinksCardProps) {
  const { data: links = [], isLoading } = useGetSocialLinksQuery({ subjectType, subjectId });

  const [fetchConnectUrl] = useLazyGetSocialConnectUrlQuery();
  const [updateHandle] = useUpdateSocialHandleMutation();
  const [requestCode, { isLoading: isRequestingCode }] = useRequestSocialVerificationCodeMutation();
  const [verifyCode, { isLoading: isVerifying }] = useVerifySocialBioCodeMutation();
  const [disconnect] = useDisconnectSocialAccountMutation();

  const [handleDrafts, setHandleDrafts] = useState<Record<string, string>>({});
  const [codeDialog, setCodeDialog] = useState<{ platform: SocialPlatform; code: string; expiresAt: string } | null>(null);

  const visibleLinks = links.filter((l) => platforms.includes(l.platform));
  const rows: SocialLinkStatus[] = platforms.map(
    (platform) =>
      visibleLinks.find((l) => l.platform === platform) ?? {
        platform,
        handle: null,
        isVerified: false,
        verifiedAt: null,
        verificationMethod: null,
        isOAuthConfigured: false,
        isManualCheckSupported: false,
        hasPendingCode: false,
        pendingCodeExpiresAt: null,
      },
  );

  function handleDraftFor(row: SocialLinkStatus): string {
    return handleDrafts[row.platform] ?? row.handle ?? "";
  }

  async function saveHandleIfChanged(row: SocialLinkStatus) {
    const draft = handleDraftFor(row).trim();
    if (!draft || draft === (row.handle ?? "")) return;
    const result = await updateHandle({ subjectType, subjectId, platform: row.platform, handle: draft });
    if ("error" in result) toast.error(`Failed to save ${SOCIAL_PLATFORM_LABEL[row.platform]} handle.`);
  }

  async function handleConnect(platform: SocialPlatform) {
    // Open the tab synchronously, in direct response to the click — matches
    // InstagramTab.tsx's existing popup-window pattern so this doesn't get
    // silently popup-blocked.
    const popup = window.open("about:blank", "_blank");

    const result = await fetchConnectUrl({ subjectType, subjectId, platform });
    if ("data" in result && result.data) {
      if (popup) {
        popup.location.href = result.data.authUrl;
      } else {
        toast.error("Pop-up blocked. Please allow pop-ups for this site and try again.");
      }
    } else {
      popup?.close();
      toast.error(`Failed to start ${SOCIAL_PLATFORM_LABEL[platform]} connection.`);
    }
  }

  async function handleRequestCode(row: SocialLinkStatus) {
    await saveHandleIfChanged(row);
    const result = await requestCode({ subjectType, subjectId, platform: row.platform });
    if ("data" in result && result.data) {
      setCodeDialog({ platform: row.platform, code: result.data.code, expiresAt: result.data.expiresAt });
    } else {
      toast.error("Set a handle first, then request a code.");
    }
  }

  async function handleVerify(platform: SocialPlatform) {
    const result = await verifyCode({ subjectType, subjectId, platform });
    if ("data" in result && result.data) {
      if (result.data.verified) {
        toast.success(`${SOCIAL_PLATFORM_LABEL[platform]} verified!`);
        setCodeDialog(null);
      } else {
        toast.error(result.data.failureReason ?? "Verification failed.");
      }
    } else {
      toast.error("Verification failed.");
    }
  }

  async function handleDisconnect(platform: SocialPlatform) {
    if (!window.confirm(`Disconnect ${SOCIAL_PLATFORM_LABEL[platform]}? The handle stays, but the Verified badge will be removed.`))
      return;
    const result = await disconnect({ subjectType, subjectId, platform });
    if ("error" in result) toast.error("Failed to disconnect.");
    else toast.success("Disconnected.");
  }

  if (isLoading) {
    return (
      <div className="space-y-2">
        {platforms.map((p) => <Skeleton key={p} className="h-14 w-full" />)}
      </div>
    );
  }

  return (
    <div className="space-y-3">
      {rows.map((row) => {
        const Icon = SOCIAL_PLATFORM_ICON[row.platform];
        const label = SOCIAL_PLATFORM_LABEL[row.platform];

        return (
          <Card key={row.platform}>
            <CardContent className="p-4 flex items-center gap-3 flex-wrap">
              <Icon className="h-5 w-5 text-muted-foreground shrink-0" aria-hidden="true" />

              <div className="flex-1 min-w-[180px] flex items-center gap-2">
                <span className="text-sm font-medium w-20 shrink-0">{label}</span>
                {row.isVerified || !canManage ? (
                  <span className="text-sm">{row.handle ? `@${row.handle}` : "—"}</span>
                ) : (
                  <Input
                    value={handleDraftFor(row)}
                    onChange={(e) => setHandleDrafts((d) => ({ ...d, [row.platform]: e.target.value }))}
                    onBlur={() => void saveHandleIfChanged(row)}
                    placeholder="handle"
                    className="h-8 text-sm max-w-[220px]"
                    aria-label={`${label} handle`}
                  />
                )}
                {row.isVerified && <VerifiedSocialBadge platform={label} />}
              </div>

              {!canManage ? null : row.isVerified ? (
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => void handleDisconnect(row.platform)}
                  className="gap-1.5 text-destructive-text hover:text-destructive-text"
                >
                  <Unlink className="h-3.5 w-3.5" aria-hidden="true" />
                  Disconnect
                </Button>
              ) : row.isOAuthConfigured ? (
                <Button size="sm" onClick={() => void handleConnect(row.platform)} className="gap-1.5">
                  Connect
                  <ExternalLink className="h-3 w-3" aria-hidden="true" />
                </Button>
              ) : row.isManualCheckSupported ? (
                <Button
                  size="sm"
                  variant="outline"
                  disabled={isRequestingCode || !handleDraftFor(row).trim()}
                  onClick={() => void handleRequestCode(row)}
                >
                  Get verification code
                </Button>
              ) : (
                <span
                  className="text-xs text-muted-foreground"
                  title={`${label} isn't connected on this server yet.`}
                >
                  Not available yet
                </span>
              )}
            </CardContent>
          </Card>
        );
      })}

      <Dialog open={codeDialog !== null} onOpenChange={(open) => !open && setCodeDialog(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              Verify {codeDialog ? SOCIAL_PLATFORM_LABEL[codeDialog.platform] : ""}
            </DialogTitle>
            <DialogDescription>
              Add this code to your {codeDialog ? SOCIAL_PLATFORM_LABEL[codeDialog.platform] : ""} bio,
              then click Verify. It can take a minute to update on their side.
            </DialogDescription>
          </DialogHeader>

          {codeDialog && (
            <div className="flex items-center gap-2">
              <code className="flex-1 rounded-md border bg-muted px-3 py-2 text-lg font-mono tracking-wide">
                {codeDialog.code}
              </code>
              <Button
                variant="outline"
                size="icon"
                type="button"
                onClick={() => {
                  void navigator.clipboard.writeText(codeDialog.code);
                  toast.success("Code copied.");
                }}
                aria-label="Copy code"
              >
                <Copy className="h-4 w-4" aria-hidden="true" />
              </Button>
            </div>
          )}

          <DialogFooter>
            <Button variant="ghost" onClick={() => setCodeDialog(null)}>Close</Button>
            <Button
              disabled={isVerifying}
              onClick={() => codeDialog && void handleVerify(codeDialog.platform)}
            >
              {isVerifying ? "Checking…" : "I've added it — Verify"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
