export { SchedulePage } from "./components/SchedulePage";
export { BookPage } from "./components/BookPage";
export { DepositStatusBadge } from "./components/DepositStatusBadge";
export { appointmentsApi } from "./appointmentsApi";
export {
  useGetAppointmentsQuery,
  useCreateAppointmentMutation,
  useCancelAppointmentMutation,
} from "./appointmentsApi";
export type { AppointmentResponse, CreateAppointmentRequest } from "./appointment.types";
export { AppointmentStatus, DepositStatus } from "./appointment.types";
