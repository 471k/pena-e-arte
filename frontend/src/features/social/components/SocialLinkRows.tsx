import { useState } from "react";
import { toast } from "sonner";
import { Copy } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import {
  SOCIAL_PLATFORM_FALLBACK_ICON,
  SOCIAL_PLATFORM_ICON,
  SOCIAL_PLATFORM_LABEL,
} from "@/shared/utils/socialPlatforms";
import {
  useLazyGetSocialConnectUrlQuery,
  useUpdateSocialHandleMutation,
  useRequestSocialVerificationCodeMutation,
  useVerifySocialBioCodeMutation,
  useDisconnectSocialAccountMutation,
  type SocialSubjectType,
  type SocialPlatform,
  type SocialLinkStatus,
} from "../socialApi";
import {
  ConnectionRow,
  type ConnectionRowAction,
  type ConnectionRowHandleInput,
} from "./ConnectionRow";
import { ConfirmDisconnectDialog } from "./ConfirmDisconnectDialog";

interface SocialLinkRowsProps {
  subjectType: SocialSubjectType;
  subjectId:   string;
  /** Which platforms to render rows for, in order. */
  platforms: readonly SocialPlatform[];
  /** Already-fetched links for the subject (the parent owns the query so it can show one loading/error state). */
  links: readonly SocialLinkStatus[];
  /** Whether the viewer may connect/verify/edit/disconnect. false renders every row read-only. */
  canManage: boolean;
  /** True when the viewer is looking at their own profile (second-person copy in dialogs). */
  isSelf?: boolean;
}

type HandleUiState = { status: "idle" | "saving" | "saved"; error: string | null };

const IDLE: HandleUiState = { status: "idle", error: null };

/**
 * The <li> rows (plus the verification-code and disconnect dialogs) for a set of social platforms.
 * Renders no <ul> of its own so a caller can put other rows (Instagram's photo-sync row) in the
 * same list. Owns the mutations; the caller owns the query.
 */
export function SocialLinkRows({
  subjectType, subjectId, platforms, links, canManage, isSelf = false,
}: SocialLinkRowsProps) {
  const [fetchConnectUrl] = useLazyGetSocialConnectUrlQuery();
  const [updateHandle] = useUpdateSocialHandleMutation();
  const [requestCode, { isLoading: isRequestingCode }] = useRequestSocialVerificationCodeMutation();
  const [verifyCode, { isLoading: isVerifying }] = useVerifySocialBioCodeMutation();
  const [disconnect] = useDisconnectSocialAccountMutation();

  const [handleDrafts, setHandleDrafts] = useState<Record<string, string>>({});
  const [handleUi, setHandleUi] = useState<Record<string, HandleUiState>>({});
  const [connecting, setConnecting] = useState<SocialPlatform | null>(null);
  const [codeDialog, setCodeDialog] = useState<{ platform: SocialPlatform; code: string; expiresAt: string } | null>(null);
  const [disconnectTarget, setDisconnectTarget] = useState<SocialPlatform | null>(null);

  const rows: SocialLinkStatus[] = platforms.map(
    (platform) =>
      links.find((l) => l.platform === platform) ?? {
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

  function draftFor(row: SocialLinkStatus): string {
    return handleDrafts[row.platform] ?? row.handle ?? "";
  }

  function setUi(platform: SocialPlatform, next: HandleUiState) {
    setHandleUi((s) => ({ ...s, [platform]: next }));
  }

  async function saveHandleIfChanged(row: SocialLinkStatus): Promise<boolean> {
    const draft = draftFor(row).trim();
    if (!draft || draft === (row.handle ?? "")) return true;
    setUi(row.platform, { status: "saving", error: null });
    const result = await updateHandle({ subjectType, subjectId, platform: row.platform, handle: draft });
    if ("error" in result) {
      const label = SOCIAL_PLATFORM_LABEL[row.platform];
      setUi(row.platform, { status: "idle", error: `Couldn't save the ${label} handle. Check it and try again.` });
      toast.error(`Failed to save ${label} handle.`);
      return false;
    }
    setUi(row.platform, { status: "saved", error: null });
    return true;
  }

  async function handleConnect(platform: SocialPlatform) {
    // Open the tab synchronously, in direct response to the click, so browsers still treat it as a
    // trusted user gesture — opening it after the awaited fetch gets silently popup-blocked.
    const popup = window.open("about:blank", "_blank");
    setConnecting(platform);

    const result = await fetchConnectUrl({ subjectType, subjectId, platform });
    setConnecting(null);
    if ("data" in result && result.data) {
      if (popup) {
        popup.location.href = result.data.authUrl;
      } else {
        toast.error("Pop-up blocked. Allow pop-ups for this site and try again.");
      }
    } else {
      popup?.close();
      toast.error(`Couldn't start the ${SOCIAL_PLATFORM_LABEL[platform]} connection. Try again.`);
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

  async function confirmDisconnect() {
    if (!disconnectTarget) return;
    const platform = disconnectTarget;
    setDisconnectTarget(null);
    const result = await disconnect({ subjectType, subjectId, platform });
    if ("error" in result) toast.error("Failed to disconnect.");
    else toast.success("Disconnected.");
  }

  function actionFor(row: SocialLinkStatus): ConnectionRowAction | null {
    if (!canManage) return null;
    if (row.isVerified) return { kind: "disconnect", onClick: () => setDisconnectTarget(row.platform) };
    if (row.isOAuthConfigured) {
      return {
        kind: "connect",
        onClick: () => void handleConnect(row.platform),
        busy: connecting === row.platform,
      };
    }
    if (row.isManualCheckSupported) {
      return {
        kind: "get-code",
        onClick: () => void handleRequestCode(row),
        disabled: !draftFor(row).trim(),
        busy: isRequestingCode,
      };
    }
    return { kind: "unavailable" };
  }

  function handleInputFor(row: SocialLinkStatus): ConnectionRowHandleInput | undefined {
    // No input when there's nothing to verify against on this server (the row explains why instead).
    if (!canManage || row.isVerified) return undefined;
    if (!row.isOAuthConfigured && !row.isManualCheckSupported) return undefined;
    const ui = handleUi[row.platform] ?? IDLE;
    return {
      value: draftFor(row),
      onChange: (value) => {
        setHandleDrafts((d) => ({ ...d, [row.platform]: value }));
        if (ui.status !== "idle" || ui.error) setUi(row.platform, IDLE);
      },
      onBlur: () => void saveHandleIfChanged(row),
      error: ui.error,
      status: ui.status,
    };
  }

  const disconnectLabel = disconnectTarget ? SOCIAL_PLATFORM_LABEL[disconnectTarget] : "";

  return (
    <>
      {rows.map((row) => {
        const action = actionFor(row);
        return (
          <ConnectionRow
            key={row.platform}
            icon={SOCIAL_PLATFORM_ICON[row.platform] ?? SOCIAL_PLATFORM_FALLBACK_ICON}
            label={SOCIAL_PLATFORM_LABEL[row.platform]}
            handle={row.handle}
            isVerified={row.isVerified}
            action={action}
            handleInput={handleInputFor(row)}
            helper={action?.kind === "connect" ? "Connect to show a Verified badge." : undefined}
          />
        );
      })}

      <ConfirmDisconnectDialog
        platform={disconnectLabel}
        body={`${isSelf ? "Your" : "The"} handle stays, but the Verified badge will be removed.`}
        open={disconnectTarget !== null}
        onOpenChange={(open) => { if (!open) setDisconnectTarget(null); }}
        onConfirm={() => void confirmDisconnect()}
      />

      <Dialog open={codeDialog !== null} onOpenChange={(open) => !open && setCodeDialog(null)}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>
              Verify {codeDialog ? SOCIAL_PLATFORM_LABEL[codeDialog.platform] : ""}
            </DialogTitle>
            <DialogDescription>
              Add this code to the {codeDialog ? SOCIAL_PLATFORM_LABEL[codeDialog.platform] : ""} bio,
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
    </>
  );
}
