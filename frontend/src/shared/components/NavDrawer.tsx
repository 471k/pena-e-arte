import { NavLink } from "react-router-dom";
import { Menu } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import {
  Sheet, SheetContent, SheetHeader, SheetTitle, SheetClose,
} from "@/shared/components/ui/sheet";
import { cn } from "@/shared/utils/cn";
import type { NavItem } from "@/shared/types/navItem";

interface NavDrawerProps {
  navItems: NavItem[];
  title: string;
  open: boolean;
  onOpenChange: (open: boolean) => void;
}

export function NavDrawer({ navItems, title, open, onOpenChange }: NavDrawerProps) {
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
          <nav className="flex flex-col gap-1 mt-2" aria-label="Main navigation">
            {navItems.map(({ label, href, icon, tourId, end, badge }) => (
              <SheetClose asChild key={href}>
                <NavLink
                  to={href}
                  end={end}
                  data-tour={tourId}
                  className={({ isActive }) =>
                    cn(
                      "flex items-center gap-3 px-3 min-h-[44px] rounded-md text-sm transition-colors",
                      isActive
                        ? "bg-violet-600 text-white"
                        : "text-muted-foreground hover:text-foreground hover:bg-muted",
                    )
                  }
                >
                  {icon}
                  <span>{label}</span>
                  {!!badge && badge > 0 && (
                    <span className="ml-auto min-w-[1.25rem] rounded-full bg-destructive px-1 py-0.5 text-[10px] font-medium text-destructive-foreground text-center">
                      {badge > 99 ? "99+" : badge}
                    </span>
                  )}
                </NavLink>
              </SheetClose>
            ))}
          </nav>
        </SheetContent>
      </Sheet>
    </>
  );
}
