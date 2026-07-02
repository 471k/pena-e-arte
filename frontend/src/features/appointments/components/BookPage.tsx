import { Link } from "react-router-dom";
import { ChevronLeft } from "lucide-react";
import { Card, CardContent, CardHeader } from "@/shared/components/ui/card";
import { BookingWidget } from "@/features/booking/components/BookingWidget";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";
import { BookAppointmentForm } from "./BookAppointmentForm";
import { MyBookingsSection } from "./MyBookingsSection";

export function BookPage() {
  useDocumentMeta({ title: "Book — Pena e Artë", canonical: "/book" });

  return (
    <BookingWidget>
      <div className="bg-background flex items-start justify-center px-4 py-12">
        <div className="w-full max-w-md space-y-6">

          <Link
            to="/"
            className="inline-flex items-center gap-1 text-xs text-muted-foreground
                       hover:text-foreground transition-colors"
          >
            <ChevronLeft className="h-3.5 w-3.5" aria-hidden="true" />
            Back
          </Link>

          <Card>
            <CardHeader className="pb-2">
              <h1 className="text-base font-semibold tracking-tight">Book an appointment</h1>
              <p className="text-xs text-muted-foreground">
                Select an artist, date, and session length. Your booking is a
                request — the studio will confirm within 24 hours.
              </p>
            </CardHeader>
            <CardContent>
              <BookAppointmentForm />
            </CardContent>
          </Card>

          <MyBookingsSection />
        </div>
      </div>
    </BookingWidget>
  );
}
