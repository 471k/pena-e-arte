import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { AlertTriangle, Loader2 } from "lucide-react";
import { useAppDispatch } from "@/app/hooks";
import { logout } from "@/features/auth/authSlice";
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
import { useRequestMyDataErasureMutation } from "../clientsApi";

const CONFIRM_WORD = "DELETE";

// Client self-service "delete my account" (GDPR Art. 17). Deliberately guarded by a
// type-to-confirm dialog so it can't be triggered accidentally.
export function DeleteAccountSection() {
  const [open, setOpen] = useState(false);
  const [confirmText, setConfirmText] = useState("");
  const [eraseAccount, { isLoading }] = useRequestMyDataErasureMutation();
  const dispatch = useAppDispatch();
  const navigate = useNavigate();

  async function handleConfirm() {
    const result = await eraseAccount();
    if ("data" in result) {
      toast.success("Your account data has been scheduled for deletion.");
      setOpen(false);
      dispatch(logout());
      navigate("/");
    } else {
      toast.error("Couldn't delete your account. Please try again or contact support.");
    }
  }

  return (
    <Card className="border-destructive/40">
      <CardContent className="space-y-3 p-4">
        <div className="flex items-center gap-2">
          <AlertTriangle className="h-4 w-4 text-destructive" />
          <h3 className="text-sm font-medium text-destructive-text">Delete my account</h3>
        </div>
        <p className="text-xs text-muted-foreground">
          Permanently delete your account and personal data (your profile, body map, and consent
          records). You&apos;re signed out immediately and can&apos;t log back in; your data is then
          permanently deleted after a 30-day grace period. This cannot be undone.
        </p>
        <Button variant="destructive" size="sm" onClick={() => setOpen(true)}>
          Delete my account
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
            <DialogTitle>Delete your account?</DialogTitle>
            <DialogDescription>
              You&apos;ll be signed out immediately and won&apos;t be able to log back in. Your
              profile, body map, and consent records are permanently deleted after a 30-day grace
              period. This cannot be undone.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-1.5">
            <Label htmlFor="confirm-delete">
              Type <span className="font-semibold">{CONFIRM_WORD}</span> to confirm
            </Label>
            <Input
              id="confirm-delete"
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
              disabled={isLoading || confirmText !== CONFIRM_WORD}
            >
              {isLoading ? (
                <>
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                  Deleting…
                </>
              ) : (
                "Delete my account"
              )}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </Card>
  );
}
