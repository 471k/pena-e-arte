import { useNavigate, useParams } from "react-router-dom";
import {
  ArrowLeft, CalendarDays, Check, CreditCard, Loader2, Trash2, UserX,
} from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter,
  DialogHeader, DialogTitle,
} from "@/shared/components/ui/dialog";
import { Separator } from "@/shared/components/ui/separator";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { AppointmentStatus, DepositStatus } from "../appointment.types";
import { AppointmentStatusBadge } from "./AppointmentStatusBadge";
import { DepositStatusBadge } from "./DepositStatusBadge";
import {
  useGetAppointmentQuery,
  useCancelAppointmentMutation,
  useConfirmAppointmentMutation,
  useCompleteAppointmentMutation,
  useMarkNoShowMutation,
} from "../appointmentsApi";

function formatDateTime(dateStr: string): string {
  return new Date(dateStr).toLocaleString("en-GB", {
    weekday: "long",
    day:     "numeric",
    month:   "long",
    year:    "numeric",
    hour:    "2-digit",
    minute:  "2-digit",
  });
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

function Row({ label, value }: { label: string; value: React.ReactNode }) {
  return (
    <div className="flex justify-between items-center py-2 gap-4">
      <span className="text-sm text-muted-foreground">{label}</span>
      <span className="text-sm font-medium text-right">{value}</span>
    </div>
  );
}

const TERMINAL_STATUSES = new Set<AppointmentStatus>([
  AppointmentStatus.Cancelled,
  AppointmentStatus.Completed,
  AppointmentStatus.NoShow,
]);

export function AppointmentDetailPage() {
  const { id }       = useParams<{ id: string }>();
  const navigate     = useNavigate();
  const isArtistPlus = usePermission(Role.Artist);
  const canOwner     = usePermission(Role.Owner);
  const [cancelDialogOpen, setCancelDialogOpen] = useState(false);

  const { data: appt, isLoading, isError } = useGetAppointmentQuery(id ?? "", {
    skip: !id,
  });

  const [cancel,   { isLoading: cancelling   }] = useCancelAppointmentMutation();
  const [confirm,  { isLoading: confirming   }] = useConfirmAppointmentMutation();
  const [complete, { isLoading: completing   }] = useCompleteAppointmentMutation();
  const [noShow,   { isLoading: markingNoShow }] = useMarkNoShowMutation();

  const isTerminal  = appt ? TERMINAL_STATUSES.has(appt.status) : false;
  const isPending   = appt?.status === AppointmentStatus.Pending;
  const isConfirmed = appt?.status === AppointmentStatus.Confirmed;
  const anyLoading  = cancelling || confirming || completing || markingNoShow;

  async function handleCancelConfirmed() {
    const result = await cancel(appt!.id);
    setCancelDialogOpen(false);
    if ("data" in result) {
      toast.success("Appointment cancelled.");
      navigate(-1);
    } else {
      toast.error("Failed to cancel appointment.");
    }
  }

  return (
    <div className="min-h-screen bg-background">
      <header className="flex items-center gap-3 px-6 py-3 border-b bg-background sticky top-0 z-10">
        <Button variant="ghost" size="icon" onClick={() => navigate(-1)} aria-label="Back">
          <ArrowLeft className="h-4 w-4" />
        </Button>
        <div className="flex items-center gap-2">
          <CalendarDays className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Appointment</span>
        </div>
        {appt && <AppointmentStatusBadge status={appt.status} />}
      </header>

      <main className="max-w-lg mx-auto px-4 py-6 space-y-4">
        {isLoading && (
          <div className="flex items-center justify-center py-16 text-muted-foreground gap-2">
            <Loader2 className="h-5 w-5 animate-spin" />
            <span className="text-sm">Loading…</span>
          </div>
        )}

        {!isLoading && (isError || !appt) && (
          <div className="flex flex-col items-center py-16 gap-3">
            <p className="text-sm text-destructive">Appointment not found.</p>
          </div>
        )}

        {!isLoading && appt && (
          <>
            <Card>
              <CardContent className="px-4 py-1">
                <Row label="Date &amp; time" value={formatDateTime(appt.date)} />
                <Separator />
                <Row label="Duration"  value={`${appt.durationMinutes} min`} />
                <Separator />
                <Row label="Status"    value={<AppointmentStatusBadge status={appt.status} />} />
                <Separator />
                <Row
                  label="Deposit"
                  value={
                    <span className="flex items-center gap-1.5">
                      {formatCurrency(appt.depositAmount)}
                      <DepositStatusBadge status={appt.depositStatus} />
                    </span>
                  }
                />
                {appt.notes && (
                  <>
                    <Separator />
                    <Row label="Notes" value={appt.notes} />
                  </>
                )}
              </CardContent>
            </Card>

            {isArtistPlus && !isTerminal && (
              <div className="flex flex-col gap-2">
                {isPending && (
                  <Button
                    className="w-full gap-2"
                    disabled={anyLoading}
                    onClick={() => confirm(appt.id)}
                  >
                    {confirming ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
                    Confirm appointment
                  </Button>
                )}

                {isConfirmed && (
                  <Button
                    className="w-full gap-2"
                    disabled={anyLoading}
                    onClick={() => complete(appt.id)}
                  >
                    {completing ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
                    Mark as complete
                  </Button>
                )}

                {appt.depositStatus === DepositStatus.Pending && canOwner && (
                  <Button
                    variant="outline"
                    className="w-full gap-2"
                    onClick={() =>
                      navigate(
                        `/payments/new?appointmentId=${appt.id}&clientId=${appt.clientId}&amount=${appt.depositAmount}`,
                      )
                    }
                  >
                    <CreditCard className="h-4 w-4" />
                    Charge deposit
                  </Button>
                )}

                {!isPending && (
                  <Button
                    variant="ghost"
                    className="w-full gap-2 text-muted-foreground"
                    disabled={anyLoading}
                    onClick={() => noShow(appt.id)}
                  >
                    {markingNoShow ? <Loader2 className="h-4 w-4 animate-spin" /> : <UserX className="h-4 w-4" />}
                    Mark no-show
                  </Button>
                )}

                <Button
                  variant="ghost"
                  className="w-full gap-2 text-destructive hover:text-destructive"
                  disabled={anyLoading}
                  onClick={() => setCancelDialogOpen(true)}
                >
                  <Trash2 className="h-4 w-4" />
                  Cancel appointment
                </Button>
              </div>
            )}
          </>
        )}
      </main>

      <Dialog open={cancelDialogOpen} onOpenChange={setCancelDialogOpen}>
        <DialogContent>
          <DialogHeader>
            <DialogTitle>Cancel appointment</DialogTitle>
            <DialogDescription>
              This will cancel the appointment. This action cannot be undone.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setCancelDialogOpen(false)}>
              Keep
            </Button>
            <Button
              variant="destructive"
              disabled={cancelling}
              onClick={handleCancelConfirmed}
            >
              {cancelling ? <Loader2 className="h-4 w-4 animate-spin" /> : "Cancel appointment"}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  );
}
