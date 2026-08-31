import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { toast } from "sonner";
import { CalendarClock, Check, CreditCard, ImageIcon, Loader2, Trash2, UserX } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { cn } from "@/shared/utils/cn";
import { AppointmentStatus, DepositStatus } from "../appointment.types";
import type { AppointmentResponse } from "../appointment.types";
import { AppointmentStatusBadge } from "./AppointmentStatusBadge";
import { DepositStatusBadge } from "./DepositStatusBadge";
import { RescheduleDialog } from "./RescheduleDialog";
import {
  useCancelAppointmentMutation,
  useConfirmAppointmentMutation,
  useCompleteAppointmentMutation,
  useMarkNoShowMutation,
} from "../appointmentsApi";

interface AppointmentCardProps {
  appointment: AppointmentResponse;
}

const TERMINAL_STATUSES = new Set<AppointmentStatus>([
  AppointmentStatus.Cancelled,
  AppointmentStatus.Completed,
  AppointmentStatus.NoShow,
]);

const STATUS_BORDER: Record<AppointmentStatus, string> = {
  Confirmed: "border-l-4 border-l-green-500/60",
  Pending:   "border-l-4 border-l-amber-500/60",
  Completed: "border-l-4 border-l-muted-foreground/30 opacity-60",
  Cancelled: "border-l-4 border-l-destructive/30 opacity-50",
  NoShow:    "border-l-4 border-l-destructive/30 opacity-50",
};

function formatTime(dateStr: string): string {
  return new Date(dateStr).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

export function AppointmentCard({ appointment }: AppointmentCardProps) {
  const navigate      = useNavigate();
  const isArtistPlus  = usePermission(Role.Artist);
  const canOwner      = usePermission(Role.Owner);
  const [confirmCancel, setConfirmCancel] = useState(false);
  const [rescheduleDialogOpen, setRescheduleDialogOpen] = useState(false);

  const [cancel,   { isLoading: cancelling  }] = useCancelAppointmentMutation();
  const [confirm,  { isLoading: confirming  }] = useConfirmAppointmentMutation();
  const [complete, { isLoading: completing  }] = useCompleteAppointmentMutation();
  const [noShow,   { isLoading: markingNoShow }] = useMarkNoShowMutation();

  const isTerminal       = TERMINAL_STATUSES.has(appointment.status);
  const isPending        = appointment.status === AppointmentStatus.Pending;
  const isConfirmed      = appointment.status === AppointmentStatus.Confirmed;
  const showChargeButton =
    canOwner &&
    !isTerminal &&
    appointment.depositStatus === DepositStatus.Pending;

  const anyLoading = cancelling || confirming || completing || markingNoShow;

  // Attachments fall back to the deprecated flat imageUrls for pre-migration appointments.
  const attachmentCount = appointment.attachments?.length ?? appointment.imageUrls?.length ?? 0;

  async function handleCancel() {
    if (!confirmCancel) { setConfirmCancel(true); return; }
    const result = await cancel(appointment.id);
    setConfirmCancel(false);
    if ("data" in result) toast.success("Appointment cancelled.");
    else                  toast.error("Failed to cancel appointment.");
  }

  async function handleConfirm() {
    const result = await confirm(appointment.id);
    if ("data" in result) toast.success("Appointment confirmed.");
    else                  toast.error("Failed to confirm appointment.");
  }

  async function handleComplete() {
    const result = await complete(appointment.id);
    if ("data" in result) toast.success("Appointment marked complete.");
    else                  toast.error("Failed to complete appointment.");
  }

  async function handleNoShow() {
    const result = await noShow(appointment.id);
    if ("data" in result) toast.success("Appointment marked as no-show.");
    else                  toast.error("Failed to mark appointment as no-show.");
  }

  return (
    <Card
      className={cn(
        "cursor-pointer hover:border-ring/50 transition-colors",
        STATUS_BORDER[appointment.status],
      )}
      onClick={() => navigate(`/appointments/${appointment.id}`)}
    >
      <CardContent className="p-4 flex items-start justify-between gap-4">
        <div className="space-y-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="font-medium text-sm">
              {formatTime(appointment.date)} – {formatTime(appointment.endDate)}
            </span>
            {appointment.clientName && (
              <span className="text-sm truncate">{appointment.clientName}</span>
            )}
            <span className="text-xs text-muted-foreground">{appointment.durationMinutes} min</span>
            <AppointmentStatusBadge status={appointment.status} />
            {appointment.status === AppointmentStatus.Pending && appointment.artistId === null && (
              <span className="text-[10px] font-medium uppercase tracking-wide rounded-full
                               bg-amber-500/15 text-amber-600 px-1.5 py-0.5">
                Needs artist
              </span>
            )}
          </div>
          <div className="text-xs text-muted-foreground flex items-center gap-1.5">
            <span>Deposit: {formatCurrency(appointment.depositAmount)}</span>
            <DepositStatusBadge status={appointment.depositStatus} />
          </div>
          {(appointment.tattooDescription || appointment.notes) && (
            <p className="text-xs text-muted-foreground truncate">
              {appointment.tattooDescription || appointment.notes}
            </p>
          )}
          {attachmentCount > 0 && (
            <div className="flex items-center gap-1 text-xs text-muted-foreground">
              <ImageIcon className="h-3 w-3" aria-hidden="true" />
              <span>
                {attachmentCount} {attachmentCount === 1 ? "image" : "images"}
              </span>
            </div>
          )}
        </div>

        {isArtistPlus && !isTerminal && (
          <div
            className="flex items-center gap-1.5 shrink-0"
            onClick={(e) => e.stopPropagation()}
          >
            {isPending && appointment.artistId !== null && (
              <Button
                variant="outline"
                size="sm"
                disabled={anyLoading}
                onClick={handleConfirm}
                className="h-7 px-2 text-xs gap-1"
              >
                {confirming ? <Loader2 className="h-3 w-3 animate-spin" /> : <Check className="h-3 w-3" />}
                Confirm
              </Button>
            )}

            {isConfirmed && (
              <Button
                variant="outline"
                size="sm"
                disabled={anyLoading}
                onClick={handleComplete}
                className="h-7 px-2 text-xs gap-1"
              >
                {completing ? <Loader2 className="h-3 w-3 animate-spin" /> : <Check className="h-3 w-3" />}
                Complete
              </Button>
            )}

            <Button
              variant="ghost"
              size="sm"
              disabled={anyLoading}
              onClick={() => setRescheduleDialogOpen(true)}
              className="h-7 w-7 p-0 text-muted-foreground"
              title="Reschedule"
              aria-label="Reschedule appointment"
            >
              <CalendarClock className="h-3.5 w-3.5" />
            </Button>

            {!isPending && !isTerminal && (
              <Button
                variant="ghost"
                size="sm"
                disabled={anyLoading}
                onClick={handleNoShow}
                className="h-7 px-2 text-xs gap-1 text-muted-foreground"
                title="Mark no-show"
              >
                {markingNoShow ? <Loader2 className="h-3 w-3 animate-spin" /> : <UserX className="h-3 w-3" />}
              </Button>
            )}

            {showChargeButton && (
              <Button
                variant="outline"
                size="sm"
                onClick={() =>
                  navigate(
                    `/payments/new?appointmentId=${appointment.id}&clientId=${appointment.clientId}&amount=${appointment.depositAmount}`,
                  )
                }
                className="h-7 px-2 text-xs gap-1.5"
              >
                <CreditCard className="h-3 w-3" />
                Charge
              </Button>
            )}

            {confirmCancel ? (
              <>
                <span className="text-xs text-destructive">Cancel?</span>
                <Button
                  variant="destructive"
                  size="sm"
                  disabled={anyLoading}
                  onClick={handleCancel}
                  className="h-7 px-2 text-xs"
                >
                  {cancelling ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : "Yes"}
                </Button>
                <Button
                  variant="ghost"
                  size="sm"
                  className="h-7 px-2 text-xs"
                  onClick={() => setConfirmCancel(false)}
                >
                  No
                </Button>
              </>
            ) : (
              <Button
                variant="ghost"
                size="sm"
                disabled={anyLoading}
                onClick={handleCancel}
                className="h-7 w-7 p-0 text-muted-foreground"
                aria-label="Cancel appointment"
              >
                {cancelling ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Trash2 className="h-3.5 w-3.5" />}
              </Button>
            )}
          </div>
        )}
      </CardContent>

      <RescheduleDialog
        appointment={appointment}
        open={rescheduleDialogOpen}
        onOpenChange={setRescheduleDialogOpen}
      />
    </Card>
  );
}
