import { useEffect } from "react";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { createHubConnection } from "@/shared/signalr/createHubConnection";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { designsApi } from "@/features/designs/designsApi";
import { notificationsApi } from "@/features/notifications/notificationsApi";
import { paymentsApi } from "@/features/payments/paymentsApi";
import { remindersApi } from "@/features/reminders/remindersApi";
import { incrementUnreadIfVisible } from "@/features/notifications/incrementUnreadIfVisible";
import type { NotificationLogResponse } from "@/features/notifications/notification.types";

export function useSignalR(studioId: string | null | undefined) {
  const token    = useAppSelector((s) => s.auth.token);
  const dispatch = useAppDispatch();

  useEffect(() => {
    if (!studioId || !token) return;

    // In dev the Vite proxy doesn't reliably forward WebSocket upgrades for SignalR,
    // so connect directly to the backend. In production the hubs are on the same origin.
    const hubBase = import.meta.env.DEV ? "http://localhost:5078" : "";

    function buildConnection(path: string) {
      return createHubConnection(`${hubBase}${path}`, token!);
    }

    const scheduleConn = buildConnection("/hubs/schedule");
    const designConn   = buildConnection("/hubs/design");
    const notifConn    = buildConnection("/hubs/notification");

    // Every handler below must have a block body. A single-expression arrow function
    // (`() => dispatch(...)`) implicitly returns dispatch's return value, which SignalR's
    // client then tries to send back to the server as an invocation *result* — but these
    // are fire-and-forget server-to-client pushes the server never asked for a result on.
    // That mismatch doesn't just log a warning: it has been observed to disrupt processing
    // of the next message on the same connection, silently dropping it client-side.
    scheduleConn.on("AppointmentCreated",   () => { dispatch(appointmentsApi.util.invalidateTags(["Appointment"])); });
    scheduleConn.on("AppointmentConfirmed", () => { dispatch(appointmentsApi.util.invalidateTags(["Appointment"])); });
    scheduleConn.on("AppointmentCompleted", () => { dispatch(appointmentsApi.util.invalidateTags(["Appointment"])); });
    scheduleConn.on("AppointmentNoShow",    () => { dispatch(appointmentsApi.util.invalidateTags(["Appointment"])); });
    scheduleConn.on("AppointmentCancelled", () => { dispatch(appointmentsApi.util.invalidateTags(["Appointment"])); });
    scheduleConn.on("DepositCaptured",      () => {
      dispatch(paymentsApi.util.invalidateTags(["Payment"]));
      dispatch(appointmentsApi.util.invalidateTags(["Appointment"]));
    });
    scheduleConn.on("PaymentAuthorized",    () => { dispatch(paymentsApi.util.invalidateTags(["Payment"])); });
    scheduleConn.on("PaymentRefunded",      () => { dispatch(paymentsApi.util.invalidateTags(["Payment"])); });

    designConn.on("DesignUploaded",        () => { dispatch(designsApi.util.invalidateTags(["Design"])); });
    designConn.on("DesignReviewed",        () => { dispatch(designsApi.util.invalidateTags(["Design"])); });
    designConn.on("DesignRevisionExpired", () => { dispatch(designsApi.util.invalidateTags(["Design"])); });

    notifConn.on("NotificationReceived", (pushed?: NotificationLogResponse) => {
      dispatch(notificationsApi.util.invalidateTags(["NotificationLog"]));
      // A scheduled manual reminder fires this event when its job finishes (sent or failed) —
      // refresh the reminder dialog's history so a row doesn't stay "Scheduled" after it ran.
      dispatch(remindersApi.util.invalidateTags(["ManualReminder"]));
      void incrementUnreadIfVisible(dispatch, pushed);
    });

    const scheduleStart = scheduleConn
      .start()
      .then(() => scheduleConn.invoke("JoinStudio", studioId))
      .catch(() => {});

    const designStart = designConn
      .start()
      .then(() => designConn.invoke("JoinStudio", studioId))
      .catch(() => {});

    const notifStart = notifConn
      .start()
      .then(() => notifConn.invoke("JoinStudio", studioId))
      .catch(() => {});

    return () => {
      // Wait for the start handshake to settle before stopping — calling stop()
      // mid-negotiation makes SignalR log a spurious error on StrictMode remounts.
      void scheduleStart.finally(() => scheduleConn.stop());
      void designStart.finally(  () => designConn.stop());
      void notifStart.finally(   () => notifConn.stop());
    };
  }, [studioId, token, dispatch]);
}
