export { SchedulePage } from "./components/SchedulePage";
export { BookPage } from "./components/BookPage";
export { AppointmentDetailPage } from "./components/AppointmentDetailPage";
export { DepositStatusBadge } from "./components/DepositStatusBadge";
export { appointmentsApi } from "./appointmentsApi";
export {
  useGetAppointmentsQuery,
  useGetAppointmentQuery,
  useGetMyAppointmentsQuery,
  useCreateAppointmentMutation,
  useCancelAppointmentMutation,
  useConfirmAppointmentMutation,
  useCompleteAppointmentMutation,
  useMarkNoShowMutation,
} from "./appointmentsApi";
export type { AppointmentResponse, CreateAppointmentRequest } from "./appointment.types";
export { AppointmentStatus, DepositStatus } from "./appointment.types";
