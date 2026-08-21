import { Link, useNavigate, useParams } from "react-router-dom";
import {
  ArrowLeft, CalendarClock, CalendarDays, Check, CreditCard, Download, Loader2, Send, Trash2, UserX,
} from "lucide-react";
import { useState } from "react";
import { toast } from "sonner";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Skeleton } from "@/shared/components/ui/skeleton";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter,
  DialogHeader, DialogTitle,
} from "@/shared/components/ui/dialog";
import { Separator } from "@/shared/components/ui/separator";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { AppointmentStatus, DepositStatus } from "../appointment.types";
import { AppointmentStatusBadge } from "./AppointmentStatusBadge";
import { DepositStatusBadge } from "./DepositStatusBadge";
import { RescheduleDialog } from "./RescheduleDialog";
import { ReminderDialog } from "@/features/reminders/components/ReminderDialog";
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

function buildIcsUrl(apptId: string): string {
  return `/api/v1/appointments/${apptId}/calendar.ics`;
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

function AppointmentDetailSkeleton() {
  return (
    <main className="max-w-lg mx-auto px-4 py-6 space-y-4" aria-label="Loading appointment">
      <div className="rounded-xl border bg-card p-4 space-y-3">
        {Array.from({ length: 5 }).map((_, i) => (
          <div key={i} className="flex justify-between py-1.5">
            <Skeleton className="h-4 w-24" />
            <Skeleton className="h-4 w-32" />
          </div>
        ))}
      </div>
      <Skeleton className="h-9 w-full rounded-md" />
    </main>
  );
}

export function AppointmentDetailPage() {
  useDocumentMeta({ title: "Appointment — TattooOS", canonical: "/appointments" });

  const { id }       = useParams<{ id: string }>();
  const navigate     = useNavigate();
  const isArtistPlus = usePermission(Role.Artist);
  const canOwner     = usePermission(Role.Owner);
  const [cancelDialogOpen, setCancelDialogOpen] = useState(false);
  const [rescheduleDialogOpen, setRescheduleDialogOpen] = useState(false);
  const [reminderDialogOpen, setReminderDialogOpen] = useState(false);

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

  async function handleConfirm() {
    const result = await confirm(appt!.id);
    if ("data" in result) toast.success("Appointment confirmed.");
    else                  toast.error("Failed to confirm appointment.");
  }

  async function handleComplete() {
    const result = await complete(appt!.id);
    if ("data" in result) toast.success("Appointment marked complete.");
    else                  toast.error("Failed to complete appointment.");
  }

  async function handleNoShow() {
    const result = await noShow(appt!.id);
    if ("data" in result) toast.success("Appointment marked as no-show.");
    else                  toast.error("Failed to mark appointment as no-show.");
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

      {isLoading ? (
        <AppointmentDetailSkeleton />
      ) : (
      <main className="max-w-lg mx-auto px-4 py-6 space-y-4">
        {(isError || !appt) && (
          <div className="flex flex-col items-center py-16 gap-3">
            <p className="text-sm text-destructive">Appointment not found.</p>
          </div>
        )}

        {appt && (
          <>
            <Card>
              <CardContent className="px-4 py-1">
                {appt.clientName && (
                  <>
                    <Row
                      label="Client"
                      value={
                        <Link
                          to={`/clients/${appt.clientId}`}
                          className="text-violet-500 hover:underline"
                        >
                          {appt.clientName}
                        </Link>
                      }
                    />
                    <Separator />
                  </>
                )}
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
                {!!appt.imageUrls?.length && (
                  <>
                    <Separator />
                    <div className="py-2 space-y-1.5">
                      <span className="text-sm text-muted-foreground">Reference images</span>
                      <div className="grid grid-cols-4 gap-2">
                        {appt.imageUrls.map((url) => (
                          <a
                            key={url}
                            href={url}
                            target="_blank"
                            rel="noreferrer"
                            className="block aspect-square rounded-md overflow-hidden
                                       border border-border/40 bg-muted/30"
                          >
                            <img
                              src={url}
                              alt="Reference image"
                              className="h-full w-full object-cover"
                            />
                          </a>
                        ))}
                      </div>
                    </div>
                  </>
                )}
                {appt.cancellationReason && (
                  <>
                    <Separator />
                    <Row label="Cancellation reason" value={appt.cancellationReason} />
                  </>
                )}
                {appt.status === AppointmentStatus.Completed && (
                  <>
                    <Separator />
                    <Row
                      label="Aftercare"
                      value={
                        appt.aftercareSentAt
                          ? <span className="text-emerald-600 text-xs font-medium">Sent ✓</span>
                          : <span className="text-muted-foreground text-xs">Pending</span>
                      }
                    />
                  </>
                )}
              </CardContent>
            </Card>

            {/* P-09: Add to Calendar */}
            <a
              href={buildIcsUrl(appt.id)}
              download
              className="flex items-center justify-center gap-2 text-sm text-muted-foreground hover:text-foreground transition-colors py-1"
              aria-label="Add to calendar"
            >
              <Download className="h-4 w-4" aria-hidden="true" />
              Add to Calendar (.ics)
            </a>

            {isArtistPlus && !isTerminal && (
              <div className="flex flex-col gap-2">
                {isPending && (
                  <Button
                    className="w-full gap-2"
                    disabled={anyLoading}
                    onClick={handleConfirm}
                  >
                    {confirming ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
                    Confirm appointment
                  </Button>
                )}

                {isConfirmed && (
                  <Button
                    className="w-full gap-2"
                    disabled={anyLoading}
                    onClick={handleComplete}
                  >
                    {completing ? <Loader2 className="h-4 w-4 animate-spin" /> : <Check className="h-4 w-4" />}
                    Mark as complete
                  </Button>
                )}

                <Button
                  variant="outline"
                  className="w-full gap-2"
                  disabled={anyLoading}
                  onClick={() => setRescheduleDialogOpen(true)}
                >
                  <CalendarClock className="h-4 w-4" />
                  Reschedule
                </Button>

                <Button
                  variant="outline"
                  className="w-full gap-2"
                  disabled={anyLoading}
                  onClick={() => setReminderDialogOpen(true)}
                >
                  <Send className="h-4 w-4" />
                  Send Reminder
                </Button>

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
                    onClick={handleNoShow}
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
      )}

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

      {appt && (
        <RescheduleDialog
          appointment={appt}
          open={rescheduleDialogOpen}
          onOpenChange={setRescheduleDialogOpen}
        />
      )}

      {appt && (
        <ReminderDialog
          appointmentId={appt.id}
          open={reminderDialogOpen}
          onOpenChange={setReminderDialogOpen}
        />
      )}
    </div>
  );
}
