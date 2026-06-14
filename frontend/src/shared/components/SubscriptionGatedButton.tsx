import type { ComponentProps } from "react";
import { Link } from "react-router-dom";
import { Button } from "@/shared/components/ui/button";
import { cn } from "@/shared/utils/cn";
import { useSubscriptionGuard } from "@/features/billing/useSubscriptionGuard";

type Props = ComponentProps<typeof Button>;

export function SubscriptionGatedButton({ children, disabled, className, ...props }: Props) {
  const { isReadOnly, cause } = useSubscriptionGuard();

  if (!isReadOnly) {
    return (
      <Button disabled={disabled} className={className} {...props}>
        {children}
      </Button>
    );
  }

  return (
    <div className="relative group">
      <Button {...props} className={cn(className)} disabled>
        {children}
      </Button>
      <span
        role="tooltip"
        className={[
          "pointer-events-none group-hover:pointer-events-auto",
          "absolute top-full left-1/2 -translate-x-1/2 mt-2 z-50",
          "rounded-md border bg-popover px-3 py-1.5",
          "text-xs text-popover-foreground shadow-md text-center",
          "w-max max-w-[220px] whitespace-normal",
          "opacity-0 group-hover:opacity-100 transition-opacity",
        ].join(" ")}
      >
        {cause === "suspended" ? (
          "Studio suspended by platform admin."
        ) : (
          <>
            Subscription inactive.{" "}
            <Link to="/billing/subscribe" className="underline hover:text-foreground">
              Subscribe
            </Link>
          </>
        )}
      </span>
    </div>
  );
}
