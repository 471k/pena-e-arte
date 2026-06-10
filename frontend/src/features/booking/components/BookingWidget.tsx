import type { ReactNode } from "react";
import { useGetMyStudioQuery } from "@/features/studios/studiosApi";

interface BookingWidgetProps {
  children: ReactNode;
}

export function BookingWidget({ children }: BookingWidgetProps) {
  const { data: studio } = useGetMyStudioQuery();

  return (
    <div className="flex flex-col min-h-screen">
      <div className="flex-1">{children}</div>
      {studio?.showPlatformBranding && (
        <footer className="py-3 text-center text-xs text-muted-foreground border-t">
          <a
            href="https://penaearte.com"
            target="_blank"
            rel="noopener noreferrer"
            className="hover:underline"
          >
            Powered by Pena e Artë
          </a>
        </footer>
      )}
    </div>
  );
}
