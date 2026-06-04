import { useState } from "react";
import { Loader2, Trash2 } from "lucide-react";
import { Card, CardContent } from "@/shared/components/ui/card";
import { Button } from "@/shared/components/ui/button";
import { usePermission } from "@/shared/hooks/usePermission";
import { Role } from "@/shared/types/roles";
import { AppointmentStatus } from "../appointment.types";
import type { AppointmentResponse } from "../appointment.types";
import { AppointmentStatusBadge } from "./AppointmentStatusBadge";
import { DepositStatusBadge } from "./DepositStatusBadge";
import { useCancelAppointmentMutation } from "../appointmentsApi";

interface AppointmentCardProps {
  appointment: AppointmentResponse;
}

const TERMINAL_STATUSES = new Set<AppointmentStatus>([
  AppointmentStatus.Cancelled,
  AppointmentStatus.Completed,
  AppointmentStatus.NoShow,
]);

function formatTime(dateStr: string): string {
  return new Date(dateStr).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

function formatCurrency(amount: number): string {
  return new Intl.NumberFormat("pt-PT", { style: "currency", currency: "EUR" }).format(amount);
}

export function AppointmentCard({ appointment }: AppointmentCardProps) {
  const canCancel = usePermission(Role.Artist);
  const [confirmCancel, setConfirmCancel] = useState(false);
  const [cancel, { isLoading }] = useCancelAppointmentMutation();

  const isTerminal = TERMINAL_STATUSES.has(appointment.status);

  async function handleCancel() {
    if (!confirmCancel) {
      setConfirmCancel(true);
      return;
    }
    await cancel(appointment.id);
    setConfirmCancel(false);
  }

  return (
    <Card>
      <CardContent className="p-4 flex items-start justify-between gap-4">
        <div className="space-y-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <span className="font-medium text-sm">
              {formatTime(appointment.date)} – {formatTime(appointment.endDate)}
            </span>
            <span className="text-xs text-muted-foreground">{appointment.durationMinutes} min</span>
            <AppointmentStatusBadge status={appointment.status} />
          </div>
          <p className="text-xs text-muted-foreground flex items-center gap-1.5">
            Deposit: {formatCurrency(appointment.depositAmount)}
            <DepositStatusBadge status={appointment.depositStatus} />
          </p>
          {appointment.notes && (
            <p className="text-xs text-muted-foreground truncate">{appointment.notes}</p>
          )}
        </div>

        {canCancel && !isTerminal && (
          <div className="flex items-center gap-1.5 shrink-0">
            {confirmCancel && (
              <>
                <span className="text-xs text-destructive">Cancel this?</span>
                <Button
                  variant="destructive"
                  size="sm"
                  disabled={isLoading}
                  onClick={handleCancel}
                  className="h-7 px-2 text-xs"
                >
                  {isLoading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : "Yes"}
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
            )}
            {!confirmCancel && (
              <Button
                variant="ghost"
                size="sm"
                disabled={isLoading}
                onClick={handleCancel}
                className="h-7 w-7 p-0"
                aria-label="Cancel appointment"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </Button>
            )}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
