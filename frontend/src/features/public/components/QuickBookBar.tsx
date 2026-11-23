import { Link } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { cn } from "@/shared/utils/cn";

const BAR = "inset-x-0 border-t bg-background/95 backdrop-blur-sm px-4 py-3 safe-area-inset-bottom page-lg:hidden";
const BUTTON = "w-full bg-violet-600 hover:bg-violet-700 text-white border-0 min-h-[44px] text-sm font-semibold";

/**
 * The public portfolio pages' booking button, kept on screen below page-lg (1100), where the page's own Book
 * button sits in a sidebar that stacks after the portfolio. One component for the artist and studio pages, so
 * the conversion control can't drift between two copies.
 *
 * It ends the page with an invisible, inert twin of itself in normal flow, so scrolled to the bottom the footer
 * sits above the bar rather than under it. The twin is the bar's own markup, so its height follows the bar's at
 * any text size, where a fixed padding (the artist page's old pb-20 on the content, above the footer) can't.
 */
export function QuickBookBar({ to, label }: { to: string; label: string }) {
  return (
    <>
      <div aria-hidden="true" inert className={cn(BAR, "invisible")} data-testid="quick-book-spacer">
        {/* The same Button, so the twin's height follows the bar's at any text size (Button sets its own height). */}
        <Button className={BUTTON} asChild tabIndex={-1}><span>{label}</span></Button>
      </div>
      <div className={cn(BAR, "fixed bottom-0 z-[90]")} aria-label="Quick book bar">
        <Button className={BUTTON} asChild>
          <Link to={to}>{label}</Link>
        </Button>
      </div>
    </>
  );
}
