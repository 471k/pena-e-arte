import { useState } from "react";
import { Bell, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import {
  Dialog, DialogContent, DialogHeader, DialogTitle, DialogDescription, DialogFooter, DialogTrigger,
} from "@/shared/components/ui/dialog";
import { Input } from "@/shared/components/ui/input";
import { Label } from "@/shared/components/ui/label";
import { Textarea } from "@/shared/components/ui/textarea";
import { useJoinWaitlistMutation } from "../waitlistApi";

interface NotifyMeDialogProps {
  studioSlug: string | null;
  artistId: string | null;
  preferredDate: Date | null;
  isAuthenticated: boolean;
}

/** "Notify me" CTA shown when BookAppointmentForm's slot-availability check comes back
 * unavailable — joins the studio's waitlist, auto-FIFO notified within 24h of a matching slot
 * freeing up. Guest fields are only required when the caller isn't signed in. */
export function NotifyMeDialog({ studioSlug, artistId, preferredDate, isAuthenticated }: NotifyMeDialogProps) {
  const [open, setOpen] = useState(false);
  const [guestName, setGuestName] = useState("");
  const [guestEmail, setGuestEmail] = useState("");
  const [guestPhone, setGuestPhone] = useState("");
  const [notes, setNotes] = useState("");
  const [joinWaitlist, { isLoading, isSuccess, error }] = useJoinWaitlistMutation();

  const from = preferredDate ?? new Date();
  const to = new Date(from.getTime() + 14 * 24 * 60 * 60 * 1000); // ±2 weeks around the requested date

  async function handleSubmit() {
    await joinWaitlist({
      studioSlug,
      artistId,
      preferredDateFrom: from.toISOString(),
      preferredDateTo: to.toISOString(),
      guestName: isAuthenticated ? null : guestName,
      guestEmail: isAuthenticated ? null : guestEmail,
      guestPhone: isAuthenticated ? null : guestPhone,
      notes: notes || null,
    }).unwrap().catch(() => {});
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button type="button" variant="outline" size="sm" className="gap-1.5">
          <Bell className="h-3.5 w-3.5" />
          Notify me when a slot opens up
        </Button>
      </DialogTrigger>
      <DialogContent>
        {isSuccess ? (
          <div className="py-4 text-center space-y-2">
            <Bell className="h-8 w-8 mx-auto text-green-600" />
            <DialogTitle>You're on the waitlist</DialogTitle>
            <p className="text-sm text-muted-foreground">
              We'll email you the moment a matching slot opens up — you'll have 24 hours to claim it.
            </p>
            <Button size="sm" onClick={() => setOpen(false)}>Done</Button>
          </div>
        ) : (
          <>
            <DialogHeader>
              <DialogTitle>Join the waitlist</DialogTitle>
              <DialogDescription>
                We'll notify you the moment a slot near your requested date frees up.
              </DialogDescription>
            </DialogHeader>
            <div className="space-y-3 py-2">
              {!isAuthenticated && (
                <>
                  <div className="space-y-1.5">
                    <Label htmlFor="waitlist-guest-name">Name</Label>
                    <Input id="waitlist-guest-name" value={guestName} onChange={(e) => setGuestName(e.target.value)} />
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="waitlist-guest-email">Email</Label>
                    <Input id="waitlist-guest-email" type="email" value={guestEmail} onChange={(e) => setGuestEmail(e.target.value)} />
                  </div>
                  <div className="space-y-1.5">
                    <Label htmlFor="waitlist-guest-phone">Phone</Label>
                    <Input id="waitlist-guest-phone" type="tel" value={guestPhone} onChange={(e) => setGuestPhone(e.target.value)} />
                  </div>
                </>
              )}
              <div className="space-y-1.5">
                <Label htmlFor="waitlist-notes">Notes (optional)</Label>
                <Textarea id="waitlist-notes" value={notes} onChange={(e) => setNotes(e.target.value)} rows={2} />
              </div>
              {error && (
                <p className="text-xs text-destructive-text" role="alert">
                  Something went wrong — please try again.
                </p>
              )}
            </div>
            <DialogFooter>
              <Button
                type="button"
                onClick={handleSubmit}
                disabled={isLoading || (!isAuthenticated && (!guestName || !guestEmail || !guestPhone))}
                className="gap-1.5"
              >
                {isLoading && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
                Join waitlist
              </Button>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}
