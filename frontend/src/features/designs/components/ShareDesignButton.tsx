import { useState } from "react";
import { Share2, Copy, Check, Trash2, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import { useCreateShareTokenMutation, useRevokeShareTokenMutation } from "../designsApi";
import type { DesignShareTokenResponse } from "../design.types";

interface ShareDesignButtonProps {
  revisionId: string;
}

export function ShareDesignButton({ revisionId }: ShareDesignButtonProps) {
  const [open, setOpen]           = useState(false);
  const [copied, setCopied]       = useState(false);
  const [tokenData, setTokenData] = useState<DesignShareTokenResponse | null>(null);

  const [createShareToken, { isLoading: isCreating }] = useCreateShareTokenMutation();
  const [revokeShareToken, { isLoading: isRevoking }] = useRevokeShareTokenMutation();

  async function handleOpen() {
    setOpen(true);
    if (!tokenData) {
      try {
        const result = await createShareToken(revisionId).unwrap();
        setTokenData(result);
      } catch {
        setOpen(false);
      }
    }
  }

  async function handleRevoke() {
    if (!tokenData) return;
    try {
      await revokeShareToken(tokenData.id).unwrap();
      setTokenData(null);
      setOpen(false);
    } catch {
      // keep dialog open on error
    }
  }

  async function handleCopy() {
    if (!tokenData) return;
    await navigator.clipboard.writeText(tokenData.shareUrl);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  return (
    <>
      <Button variant="outline" size="sm" onClick={handleOpen}>
        <Share2 className="h-4 w-4 mr-2" />
        Share
      </Button>

      <Dialog open={open} onOpenChange={setOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Share design</DialogTitle>
            <DialogDescription>
              Anyone with this link can view the design for 30 days.
            </DialogDescription>
          </DialogHeader>

          {isCreating ? (
            <div className="flex items-center gap-2 py-4">
              <Loader2 className="h-4 w-4 animate-spin text-muted-foreground" />
              <span className="text-sm text-muted-foreground">Generating link…</span>
            </div>
          ) : tokenData ? (
            <div className="space-y-4">
              <div className="flex items-center gap-2 p-3 bg-muted rounded-md">
                <code className="text-xs flex-1 break-all">{tokenData.shareUrl}</code>
                <Button variant="ghost" size="icon" onClick={handleCopy} className="shrink-0">
                  {copied
                    ? <Check className="h-4 w-4 text-green-500" />
                    : <Copy className="h-4 w-4" />}
                </Button>
              </div>
              <p className="text-xs text-muted-foreground">
                Expires {new Date(tokenData.expiresAt).toLocaleDateString()}
              </p>
              <Button
                variant="destructive"
                size="sm"
                onClick={handleRevoke}
                disabled={isRevoking}
              >
                {isRevoking
                  ? <Loader2 className="h-4 w-4 animate-spin mr-2" />
                  : <Trash2 className="h-4 w-4 mr-2" />}
                Revoke link
              </Button>
            </div>
          ) : null}
        </DialogContent>
      </Dialog>
    </>
  );
}
