import { useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { Loader2, Mail } from "lucide-react";
import { useAppDispatch } from "@/app/hooks";
import { useClickOutside } from "@/shared/hooks/useClickOutside";
import { useEscapeKey } from "@/shared/hooks/useEscapeKey";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel,
  AlertDialogContent, AlertDialogDescription, AlertDialogFooter,
  AlertDialogHeader, AlertDialogTitle,
} from "@/shared/components/ui/alert-dialog";
import { Button } from "@/shared/components/ui/button";
import { setCredentials } from "@/features/auth/authSlice";
import { decodeToken } from "@/shared/utils/jwt";
import {
  useGetMyJoinInvitesQuery,
  useAcceptJoinInviteMutation,
  useDeclineJoinInviteMutation,
} from "../authApi";
import type { MyStudioJoinInviteResponse } from "../authApi";

type StudioJoinInviteBellProps = {
  // Only a solo studio's owner can ever have a pending invite (server-side, an invite is only
  // ever created for an email that resolves to an IsSolo-owned account) — gate the query on
  // that so the majority of (non-solo) owners don't issue a request/DB query on every mount
  // for a result that's guaranteed empty.
  enabled: boolean;
};

// Solo-artist studio-join invites — a different domain than NotificationLog (email/SMS
// history), so this is a small, dedicated bell rather than merged into NotificationBell's
// dropdown.
export function StudioJoinInviteBell({ enabled }: StudioJoinInviteBellProps) {
  const dispatch = useAppDispatch();
  const navigate = useNavigate();
  const containerRef = useRef<HTMLDivElement>(null);
  const [isOpen, setIsOpen] = useState(false);
  const [confirmInvite, setConfirmInvite] = useState<MyStudioJoinInviteResponse | null>(null);
  const [decliningId, setDecliningId] = useState<string | null>(null);

  const { data: invites } = useGetMyJoinInvitesQuery(undefined, { skip: !enabled });
  const [acceptInvite, { isLoading: isAccepting }] = useAcceptJoinInviteMutation();
  const [declineInvite] = useDeclineJoinInviteMutation();

  const closePanel = () => setIsOpen(false);
  useClickOutside(containerRef, isOpen, closePanel);
  useEscapeKey(isOpen, closePanel);

  const count = invites?.length ?? 0;
  if (count === 0) return null;

  async function handleAccept() {
    if (!confirmInvite) return;
    try {
      const response = await acceptInvite({ inviteId: confirmInvite.id }).unwrap();
      dispatch(setCredentials({ ...decodeToken(response.accessToken), refreshToken: response.refreshToken }));
      toast.success(`You're now an artist at ${confirmInvite.studioName}.`);
      setConfirmInvite(null);
      setIsOpen(false);
      navigate("/schedule", { replace: true });
    } catch {
      toast.error("Couldn't accept this invite. Please try again.");
    }
  }

  async function handleDecline(invite: MyStudioJoinInviteResponse) {
    setDecliningId(invite.id);
    try {
      await declineInvite({ inviteId: invite.id }).unwrap();
      toast.success(`Declined the invite from ${invite.studioName}.`);
    } catch {
      toast.error("Couldn't decline this invite. Please try again.");
    } finally {
      setDecliningId(null);
    }
  }

  return (
    <div className="relative" ref={containerRef}>
      <button
        type="button"
        onClick={() => setIsOpen((v) => !v)}
        aria-label={`Studio join invites, ${count} pending`}
        aria-expanded={isOpen}
        title="Studio join invites"
        data-tour="owner-join-invite-bell"
        className="relative h-8 w-8 flex items-center justify-center rounded-md text-muted-foreground hover:text-foreground hover:bg-muted transition-colors"
      >
        <Mail className="h-4 w-4" />
        <span className="absolute -top-1 -right-1 flex h-4 min-w-4 items-center justify-center rounded-full bg-primary px-1 text-[10px] font-semibold leading-none text-primary-foreground">
          {count > 9 ? "9+" : count}
        </span>
      </button>

      {isOpen && (
        <div className="absolute right-0 top-full mt-2 w-80 rounded-md border bg-background shadow-lg z-[1100]">
          <div className="px-3 py-2 border-b">
            <span className="text-sm font-medium">Studio join invites</span>
          </div>
          <div className="max-h-80 overflow-y-auto">
            {invites?.map((invite) => (
              <div key={invite.id} className="px-3 py-2.5 border-b last:border-b-0 space-y-1.5">
                <p className="text-xs">
                  <span className="font-medium">{invite.studioName}</span>
                  {invite.studioCity && (
                    <span className="text-muted-foreground"> in {invite.studioCity}</span>
                  )}
                  {" "}wants you to join as an artist.
                </p>
                <div className="flex items-center gap-2">
                  <Button size="sm" className="h-7 text-xs" onClick={() => setConfirmInvite(invite)}>
                    Accept
                  </Button>
                  <Button
                    size="sm"
                    variant="ghost"
                    className="h-7 text-xs"
                    disabled={decliningId === invite.id}
                    onClick={() => void handleDecline(invite)}
                  >
                    {decliningId === invite.id ? "Declining…" : "Decline"}
                  </Button>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      <AlertDialog
        open={confirmInvite !== null}
        onOpenChange={(open) => !open && setConfirmInvite(null)}
      >
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Join {confirmInvite?.studioName}?</AlertDialogTitle>
            <AlertDialogDescription>
              Your current solo studio will be closed — its data is kept, but it becomes
              permanently inaccessible to you as an owner. You'll become an artist at{" "}
              {confirmInvite?.studioName} instead, using your existing login.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={isAccepting}>Cancel</AlertDialogCancel>
            <AlertDialogAction onClick={() => void handleAccept()} disabled={isAccepting}>
              {isAccepting ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : "Join studio"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
