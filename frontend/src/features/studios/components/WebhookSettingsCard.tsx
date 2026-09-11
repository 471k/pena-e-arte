import { useState } from "react";
import { AlertTriangle, Copy, Loader2, Send, Trash2, Webhook as WebhookIcon } from "lucide-react";
import { toast } from "sonner";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Badge } from "@/shared/components/ui/badge";
import {
  Table, TableBody, TableCell, TableHead, TableHeader, TableRow,
} from "@/shared/components/ui/table";
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
  useGetWebhookStatusQuery,
  useUpsertWebhookMutation,
  useDeleteWebhookMutation,
  useSendTestWebhookEventMutation,
  useGetWebhookDeliveriesQuery,
} from "../studiosApi";

function formatDateTime(iso: string): string {
  return new Date(iso).toLocaleString("en-GB", {
    day: "numeric", month: "short", year: "numeric", hour: "2-digit", minute: "2-digit",
  });
}

function extractErrorMessage(err: unknown, fallback: string): string {
  return err && typeof err === "object" && "data" in err && err.data &&
    typeof err.data === "object" && "message" in err.data
    ? String((err.data as { message: string }).message)
    : fallback;
}

export function WebhookSettingsCard() {
  const { data: studio } = useGetMyStudioQuery();
  const { data: status, isLoading: statusLoading } = useGetWebhookStatusQuery(undefined, {
    skip: !studio?.allowApiAccess,
  });
  const { data: deliveries } = useGetWebhookDeliveriesQuery(undefined, {
    skip: !studio?.allowApiAccess || !status?.hasEndpoint,
  });
  const [upsertWebhook, { isLoading: isSaving }] = useUpsertWebhookMutation();
  const [deleteWebhook, { isLoading: isDeleting }] = useDeleteWebhookMutation();
  const [sendTestEvent, { isLoading: isSendingTest }] = useSendTestWebhookEventMutation();

  const [urlInput, setUrlInput] = useState("");
  const [editing, setEditing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [newSecret, setNewSecret] = useState<string | null>(null);
  const [confirmRemoveOpen, setConfirmRemoveOpen] = useState(false);

  if (!studio) return null;

  function startEditing() {
    setUrlInput(status?.url ?? "");
    setError(null);
    setEditing(true);
  }

  async function handleSave() {
    setError(null);
    try {
      const result = await upsertWebhook({ url: urlInput.trim() }).unwrap();
      setNewSecret(result.secret);
      setEditing(false);
    } catch (err: unknown) {
      setError(extractErrorMessage(err, "Failed to save webhook URL."));
    }
  }

  async function handleRemove() {
    try {
      await deleteWebhook().unwrap();
      toast.success("Webhook removed.");
    } catch {
      toast.error("Failed to remove webhook.");
    } finally {
      setConfirmRemoveOpen(false);
    }
  }

  async function handleSendTest() {
    try {
      await sendTestEvent().unwrap();
      toast.success("Test event queued — check the delivery log below in a few seconds.");
    } catch (err: unknown) {
      toast.error(extractErrorMessage(err, "Failed to send test event."));
    }
  }

  async function copySecret() {
    if (!newSecret) return;
    await navigator.clipboard.writeText(newSecret);
    toast.success("Copied to clipboard.");
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Developer — Webhooks</CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-xs text-muted-foreground">
          Get notified the instant something happens — a booking, a cancellation, a new client —
          instead of polling the API. We POST a signed event to your URL; verify it with the
          secret shown when you save.
        </p>

        {!studio.allowApiAccess && (
          <p className="text-xs text-amber-600 dark:text-amber-400">
            Upgrade to the Pro plan to enable webhooks.
          </p>
        )}

        {studio.allowApiAccess && !statusLoading && (
          <div className="rounded-md border px-3 py-3 space-y-3">
            {status?.hasEndpoint && !editing ? (
              <>
                <div className="flex items-center gap-2 text-sm font-mono break-all">
                  <WebhookIcon className="h-3.5 w-3.5 text-muted-foreground shrink-0" aria-hidden="true" />
                  {status.url}
                </div>

                {!status.isActive && (
                  <p className="flex items-start gap-1.5 text-xs text-amber-600 dark:text-amber-400">
                    <AlertTriangle className="h-3.5 w-3.5 shrink-0 mt-0.5" aria-hidden="true" />
                    Disabled after repeated delivery failures. Re-save the URL below to reactivate.
                  </p>
                )}

                <p className="text-xs text-muted-foreground">
                  {status.lastDeliveryAt
                    ? <>Last delivery {formatDateTime(status.lastDeliveryAt)} — {status.lastDeliverySucceeded ? "succeeded" : "failed"}</>
                    : "No deliveries yet"}
                </p>

                <div className="flex flex-wrap gap-2 pt-1">
                  <Button variant="outline" size="sm" className="gap-1.5" onClick={startEditing}>
                    Edit
                  </Button>
                  <Button
                    variant="outline" size="sm" className="gap-1.5"
                    onClick={() => void handleSendTest()} disabled={isSendingTest || !status.isActive}
                  >
                    {isSendingTest ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Send className="h-3.5 w-3.5" />}
                    Send test event
                  </Button>
                  <Button
                    variant="outline" size="sm" className="gap-1.5 text-destructive-text"
                    onClick={() => setConfirmRemoveOpen(true)} disabled={isDeleting}
                  >
                    <Trash2 className="h-3.5 w-3.5" /> Remove
                  </Button>
                </div>
              </>
            ) : (
              <div className="space-y-2">
                <Input
                  placeholder="https://your-app.example.com/webhooks/pena-e-arte"
                  value={urlInput}
                  onChange={(e) => setUrlInput(e.target.value)}
                />
                {error && <p className="text-xs text-destructive-text">{error}</p>}
                <div className="flex gap-2">
                  <Button size="sm" onClick={() => void handleSave()} disabled={isSaving || !urlInput.trim()}>
                    {isSaving ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : "Save"}
                  </Button>
                  {editing && (
                    <Button variant="outline" size="sm" onClick={() => setEditing(false)} disabled={isSaving}>
                      Cancel
                    </Button>
                  )}
                </div>
              </div>
            )}
          </div>
        )}

        {studio.allowApiAccess && status?.hasEndpoint && deliveries && deliveries.length > 0 && (
          <div className="pt-1">
            <p className="text-xs font-medium text-muted-foreground mb-1.5">Recent deliveries</p>
            <div className="rounded-md border overflow-x-auto">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Event</TableHead>
                    <TableHead>Status</TableHead>
                    <TableHead>When</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {deliveries.slice(0, 10).map((d) => (
                    <TableRow key={d.id}>
                      <TableCell className="font-mono text-xs">{d.eventType}</TableCell>
                      <TableCell>
                        <Badge variant={d.succeeded ? "secondary" : "destructive"}>
                          {d.succeeded ? "Delivered" : `Failed${d.responseStatusCode ? ` (${d.responseStatusCode})` : ""}`}
                        </Badge>
                      </TableCell>
                      <TableCell className="text-xs text-muted-foreground">{formatDateTime(d.attemptedAt)}</TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            </div>
          </div>
        )}
      </CardContent>

      {/* The signing secret is shown exactly once, right after it's created. */}
      <Dialog open={!!newSecret} onOpenChange={(open) => { if (!open) setNewSecret(null); }}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Your webhook signing secret</DialogTitle>
            <DialogDescription>
              Copy this now — for security, it won't be shown again. Use it to verify the
              X-Webhook-Signature header on every delivery. Saving a new URL issues a new secret.
            </DialogDescription>
          </DialogHeader>
          <div className="flex items-center rounded-md border bg-muted/30 px-3 py-2 font-mono text-xs break-all">
            {newSecret}
          </div>
          <DialogFooter>
            <Button variant="outline" className="gap-1.5" onClick={() => void copySecret()}>
              <Copy className="h-3.5 w-3.5" /> Copy
            </Button>
            <Button onClick={() => setNewSecret(null)}>Done</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <AlertDialog open={confirmRemoveOpen} onOpenChange={setConfirmRemoveOpen}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Remove webhook?</AlertDialogTitle>
            <AlertDialogDescription>
              We'll stop sending events to this URL immediately. This can't be undone.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={isDeleting}>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={() => void handleRemove()} disabled={isDeleting}>
              {isDeleting ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : "Remove"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </Card>
  );
}
