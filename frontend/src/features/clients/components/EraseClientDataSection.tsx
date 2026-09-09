import { useState } from "react";
import { toast } from "sonner";
import { AlertTriangle, Loader2, ShieldAlert } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/shared/components/ui/dialog";
import { useRequestDataErasureMutation } from "../clientsApi";

interface EraseClientDataSectionProps {
  clientId: string;
  clientName: string;
  erasureRequestedAt: string | null;
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-GB", {
    day: "numeric", month: "short", year: "numeric",
  });
}

// Owner/support-initiated "erase this client's data" (GDPR Art. 17) — for honoring a deletion
// request that came in by phone/email, or for a walk-in client without their own account.
// Guarded by typing the client's full name so it can't be triggered against the wrong client
// by accident. Mirrors DeleteAccountSection's self-service flow.
export function EraseClientDataSection({
  clientId,
  clientName,
  erasureRequestedAt,
}: EraseClientDataSectionProps) {
  const [open, setOpen] = useState(false);
  const [confirmText, setConfirmText] = useState("");
  const [eraseData, { isLoading }] = useRequestDataErasureMutation();

  if (erasureRequestedAt) {
    return (
      <Card className="border-destructive/40">
        <CardContent className="flex items-start gap-2 p-4">
          <ShieldAlert className="h-4 w-4 shrink-0 text-destructive mt-0.5" />
          <p className="text-xs text-muted-foreground">
            Data erasure requested on {formatDate(erasureRequestedAt)}. This client&apos;s profile
            and consent records are being permanently deleted.
          </p>
        </CardContent>
      </Card>
    );
  }

  async function handleConfirm() {
    const result = await eraseData(clientId);
    if ("data" in result) {
      toast.success("Client data has been scheduled for deletion.");
      setOpen(false);
    } else {
      toast.error("Couldn't erase this client's data. Please try again or contact support.");
    }
  }

  return (
    <Card className="border-destructive/40">
      <CardContent className="space-y-3 p-4">
        <div className="flex items-center gap-2">
          <AlertTriangle className="h-4 w-4 text-destructive" />
          <h3 className="text-sm font-medium text-destructive-text">Erase client data</h3>
        </div>
        <p className="text-xs text-muted-foreground">
          Permanently delete this client&apos;s profile, body map, and consent records — e.g. to
          honor a deletion request received by phone or email. If they have a login, it&apos;s
          disabled immediately. Data is permanently deleted after a 30-day grace period. This
          cannot be undone.
        </p>
        <Button variant="destructive" size="sm" onClick={() => setOpen(true)}>
          Erase client data
        </Button>
      </CardContent>

      <Dialog
        open={open}
        onOpenChange={(next) => {
          setOpen(next);
          if (!next) setConfirmText("");
        }}
      >
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Erase {clientName}&apos;s data?</DialogTitle>
            <DialogDescription>
              Their profile, body map, and consent records are permanently deleted after a 30-day
              grace period, and their login (if any) is disabled immediately. This cannot be
              undone.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-1.5">
            <Label htmlFor="confirm-erase-client">
              Type <span className="font-semibold">{clientName}</span> to confirm
            </Label>
            <Input
              id="confirm-erase-client"
              value={confirmText}
              onChange={(e) => setConfirmText(e.target.value)}
              autoComplete="off"
              disabled={isLoading}
            />
          </div>

          <DialogFooter>
            <Button variant="outline" onClick={() => setOpen(false)} disabled={isLoading}>
              Cancel
            </Button>
            <Button
              variant="destructive"
              onClick={handleConfirm}
              disabled={isLoading || confirmText !== clientName}
            >
              {isLoading ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  Erasing…
                </>
              ) : (
                "Erase client data"
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </Card>
  );
}
