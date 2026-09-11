import { useState } from "react";
import { Copy, Key, Loader2, RefreshCw, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from "@/shared/components/ui/alert-dialog";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/shared/components/ui/dialog";
import {
  useGetMyStudioQuery,
  useGetApiKeyStatusQuery,
  useGenerateApiKeyMutation,
  useRevokeApiKeyMutation,
} from "../studiosApi";

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", { day: "numeric", month: "short", year: "numeric" });
}

export function DeveloperSettingsCard() {
  const { data: studio } = useGetMyStudioQuery();
  const { data: keyStatus, isLoading: statusLoading } = useGetApiKeyStatusQuery(undefined, {
    skip: !studio?.allowApiAccess,
  });
  const [generateKey, { isLoading: isGenerating }] = useGenerateApiKeyMutation();
  const [revokeKey, { isLoading: isRevoking }] = useRevokeApiKeyMutation();

  const [newKey, setNewKey] = useState<string | null>(null);
  const [confirmRevokeOpen, setConfirmRevokeOpen] = useState(false);
  const [confirmRegenerateOpen, setConfirmRegenerateOpen] = useState(false);

  if (!studio) return null;

  async function handleGenerate() {
    try {
      const result = await generateKey().unwrap();
      setNewKey(result.apiKey);
      setConfirmRegenerateOpen(false);
    } catch {
      toast.error("Failed to generate API key.");
    }
  }

  async function handleRevoke() {
    try {
      await revokeKey().unwrap();
      toast.success("API key revoked.");
    } catch {
      toast.error("Failed to revoke API key.");
    } finally {
      setConfirmRevokeOpen(false);
    }
  }

  async function copyKey() {
    if (!newKey) return;
    await navigator.clipboard.writeText(newKey);
    toast.success("Copied to clipboard.");
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Developer — API access</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-xs text-muted-foreground">
          Connect your own tools — accounting software, spreadsheets, a custom integration — to
          your studio's appointment, client, and revenue data. Read-only: nothing can be created,
          changed, or deleted through this key.
        </p>

        {!studio.allowApiAccess && (
          <p className="text-xs text-amber-600 dark:text-amber-400">
            Upgrade to the Pro plan to enable API access.
          </p>
        )}

        {studio.allowApiAccess && !statusLoading && (
          <div className="rounded-md border px-3 py-3 space-y-2">
            {keyStatus?.hasActiveKey ? (
              <>
                <div className="flex items-center gap-2 text-sm font-mono">
                  <Key className="h-3.5 w-3.5 text-muted-foreground shrink-0" aria-hidden="true" />
                  {keyStatus.keyPrefix}…
                </div>
                <p className="text-xs text-muted-foreground">
                  Created {formatDate(keyStatus.createdAt!)}
                  {keyStatus.lastUsedAt
                    ? <> · Last used {formatDate(keyStatus.lastUsedAt)}</>
                    : <> · Never used</>}
                </p>
                <div className="flex gap-2 pt-1">
                  <Button
                    variant="outline" size="sm" className="gap-1.5"
                    onClick={() => setConfirmRegenerateOpen(true)} disabled={isGenerating}
                  >
                    <RefreshCw className="h-3.5 w-3.5" /> Regenerate
                  </Button>
                  <Button
                    variant="outline" size="sm" className="gap-1.5 text-destructive-text"
                    onClick={() => setConfirmRevokeOpen(true)} disabled={isRevoking}
                  >
                    <Trash2 className="h-3.5 w-3.5" /> Revoke
                  </Button>
                </div>
              </>
            ) : (
              <Button size="sm" className="gap-1.5" onClick={() => void handleGenerate()} disabled={isGenerating}>
                {isGenerating
                  ? <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  : <Key className="h-3.5 w-3.5" />}
                Generate API key
              </Button>
            )}
          </div>
        )}
      </CardContent>

      {/* The plaintext key is shown exactly once, right after it's created. */}
      <Dialog open={!!newKey} onOpenChange={(open) => { if (!open) setNewKey(null); }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Your new API key</DialogTitle>
            <DialogDescription>
              Copy this now — for security, it won't be shown again. If you lose it, generate a
              new one (the old one stops working immediately).
            </DialogDescription>
          </DialogHeader>
          <div className="flex items-center rounded-md border bg-muted/30 px-3 py-2 font-mono text-xs break-all">
            {newKey}
          </div>
          <DialogFooter>
            <Button variant="outline" className="gap-1.5" onClick={() => void copyKey()}>
              <Copy className="h-3.5 w-3.5" /> Copy
            </Button>
            <Button onClick={() => setNewKey(null)}>Done</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <AlertDialog open={confirmRegenerateOpen} onOpenChange={setConfirmRegenerateOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Regenerate API key?</AlertDialogTitle>
            <AlertDialogDescription>
              Your current key will stop working immediately. Any integration using it will need
              the new key.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={isGenerating}>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={() => void handleGenerate()} disabled={isGenerating}>
              {isGenerating ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : "Regenerate"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      <AlertDialog open={confirmRevokeOpen} onOpenChange={setConfirmRevokeOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Revoke API key?</AlertDialogTitle>
            <AlertDialogDescription>
              Any integration using this key will stop working immediately. This can't be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={isRevoking}>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={() => void handleRevoke()} disabled={isRevoking}>
              {isRevoking ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : "Revoke"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Card>
  );
}
