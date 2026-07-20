import { Loader2 } from "lucide-react";
import {
  useGetMyFeedbackReportsQuery,
  SupportRequestForm,
  SupportTicketThread,
} from "@/features/feedback";

const OPEN_STATUSES = new Set(["Open", "Reviewing"]);

export function ContactSupportPanel() {
  const { data: reports, isLoading } = useGetMyFeedbackReportsQuery({ type: "SupportRequest" });
  const openTicket = reports?.find((r) => OPEN_STATUSES.has(r.status));

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-12 text-muted-foreground">
        <Loader2 className="h-5 w-5 animate-spin" />
      </div>
    );
  }

  if (openTicket) {
    return <SupportTicketThread report={openTicket} />;
  }

  return <SupportRequestForm />;
}
