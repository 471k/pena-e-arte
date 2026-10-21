import { Menu } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/shared/components/ui/sheet";
import { SidebarNav } from "@/shared/components/SidebarNav";
import { isOnboardingTourEvent } from "@/shared/utils/onboardingTourDom";
import type { NavSection } from "@/shared/types/navItem";

interface NavDrawerProps {
  sections: NavSection[];
  title: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  revealTourId?: string | null;
  /** False while the onboarding tour drives the drawer — see useNavShell. */
  modal?: boolean;
}

/** Below `lg` the sidebar is replaced by this slide-in drawer, rendering the same sections and groups. */
export function NavDrawer({ sections, title, open, onOpenChange, revealTourId, modal = true }: NavDrawerProps) {
  return (
    <>
      <Button
        variant="ghost"
        size="icon"
        className="h-8 w-8 lg:hidden"
        aria-label="Open navigation menu"
        onClick={() => onOpenChange(true)}
      >
        <Menu className="h-5 w-5" />
      </Button>

      <Sheet open={open} onOpenChange={onOpenChange} modal={modal}>
        <SheetContent
          side="left"
          className="w-72 flex flex-col gap-1 overflow-y-auto"
          // The onboarding tour opens this drawer to point at nav links; a click on the tour's own
          // Next/Back buttons is "outside" the drawer for Radix and would close it mid-tour.
          onInteractOutside={(e) => { if (isOnboardingTourEvent(e.target)) e.preventDefault(); }}
        >
          <SheetHeader>
            <SheetTitle>{title}</SheetTitle>
          </SheetHeader>
          <div className="mt-2">
            <SidebarNav
              sections={sections}
              touch
              revealTourId={revealTourId}
              onNavigate={() => onOpenChange(false)}
              ariaLabel="Mobile navigation"
            />
          </div>
        </SheetContent>
      </Sheet>
    </>
  );
}
