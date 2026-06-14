import { useEffect } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { appointmentsApi } from "@/features/appointments/appointmentsApi";
import { designsApi } from "@/features/designs/designsApi";
import { notificationsApi } from "@/features/notifications/notificationsApi";

export function useSignalR(studioId: string | null | undefined) {
  const token    = useAppSelector((s) => s.auth.token);
  const dispatch = useAppDispatch();

  useEffect(() => {
    if (!studioId || !token) return;

    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/schedule", { accessTokenFactory: () => token })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on("AppointmentCreated",   () => dispatch(appointmentsApi.util.invalidateTags(["Appointment"])));
    connection.on("AppointmentConfirmed", () => dispatch(appointmentsApi.util.invalidateTags(["Appointment"])));
    connection.on("AppointmentCompleted", () => dispatch(appointmentsApi.util.invalidateTags(["Appointment"])));
    connection.on("AppointmentNoShow",    () => dispatch(appointmentsApi.util.invalidateTags(["Appointment"])));
    connection.on("AppointmentCancelled", () => dispatch(appointmentsApi.util.invalidateTags(["Appointment"])));
    connection.on("NotificationCreated",  () => dispatch(notificationsApi.util.invalidateTags(["NotificationLog"])));
    connection.on("DesignRevisionUploaded", () => {
      dispatch(designsApi.util.invalidateTags(["Design"]));
    });

    const startPromise = connection
      .start()
      .then(() => connection.invoke("JoinStudio", studioId))
      .catch(() => {
        // Connection failure is non-fatal — the app works without real-time updates
      });

    return () => {
      // Wait for the start handshake to settle before stopping — calling stop()
      // mid-negotiation makes SignalR log a spurious error on StrictMode remounts.
      void startPromise.finally(() => connection.stop());
    };
  }, [studioId, token, dispatch]);
}
