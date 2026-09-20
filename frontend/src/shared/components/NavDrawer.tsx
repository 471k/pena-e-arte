import { Menu } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle,
} from "@/shared/components/ui/sheet";
import { SidebarNav } from "@/shared/components/SidebarNav";
import type { NavSection } from "@/shared/types/navItem";

interface NavDrawerProps {
  sections: NavSection[];
  title: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
  revealTourId?: string | null;
}

/** Below `lg` the sidebar is replaced by this slide-in drawer, rendering the same sections and groups. */
export function NavDrawer({ sections, title, open, onOpenChange, revealTourId }: NavDrawerProps) {
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

      <Sheet open={open} onOpenChange={onOpenChange}>
        <SheetContent side="left" className="w-72 flex flex-col gap-1 overflow-y-auto">
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
