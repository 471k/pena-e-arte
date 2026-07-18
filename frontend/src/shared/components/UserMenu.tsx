import { useCallback, useRef, useState } from "react";
import { ChevronDown, LogOut } from "lucide-react";
import { UserChip } from "./UserChip";
import { useClickOutside } from "@/shared/hooks/useClickOutside";
import { useEscapeKey }    from "@/shared/hooks/useEscapeKey";

export function UserMenu({ onLogout }: { onLogout: () => void }) {
  const [open, setOpen] = useState(false);
  const ref = useRef<HTMLDivElement>(null);
  const close = useCallback(() => setOpen(false), []);

  useClickOutside(ref, open, close);
  useEscapeKey(open, close);

  return (
    <div ref={ref} className="relative">
      <button
        onClick={() => setOpen((v) => !v)}
        aria-label="User menu"
        aria-expanded={open}
        aria-haspopup="true"
        className="flex items-center gap-1 rounded-md px-2 py-1.5 text-sm text-muted-foreground hover:text-foreground hover:bg-muted transition-colors"
      >
        <UserChip />
        <ChevronDown className="h-3 w-3 text-muted-foreground/70" />
      </button>

      {open && (
        <div className="absolute right-0 top-full mt-1 w-40 rounded-md border bg-background shadow-md z-50 py-1">
          <button
            onClick={() => { close(); onLogout(); }}
            className="flex items-center gap-2 w-full px-3 py-2 text-sm text-muted-foreground hover:text-foreground hover:bg-muted transition-colors"
          >
            <LogOut className="h-4 w-4" />
            Log out
          </button>
        </div>
      )}
    </div>
  );
}
