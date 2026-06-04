import { PenLine } from "lucide-react";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { BookAppointmentForm } from "./BookAppointmentForm";

export function BookPage() {
  return (
    <div className="min-h-screen bg-background flex items-start justify-center px-4 py-12">
      <div className="w-full max-w-md space-y-6">
        <div className="flex items-center gap-2">
          <PenLine className="h-5 w-5" />
          <span className="font-semibold tracking-tight">Book an Appointment</span>
        </div>

        <Card>
          <CardHeader>
            <CardTitle className="text-base">New appointment</CardTitle>
          </CardHeader>
          <CardContent>
            <BookAppointmentForm />
          </CardContent>
        </Card>
      </div>
    </div>
  );
}
