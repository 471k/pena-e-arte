import { Slot } from "@radix-ui/react-slot";
import { cva, type VariantProps } from "class-variance-authority";
import { Loader2 } from "lucide-react";
import * as React from "react";
import { cn } from "@/shared/utils/cn";

// The 8-state contract (slice 6a): default, hover, focus-visible, pressed, disabled-with-explanation, loading,
// error and settled success. The first five live here, so every Button has them by construction; error and
// success are announced by the surface that caused them (LiveAnnouncement, the form's error summary, a toast).
// `aria-disabled` is styled exactly like `disabled` because `loading` and `disabledReason` use it: both keep
// the button focusable, which `disabled` would not.
export const buttonVariants = cva(
  "inline-flex items-center justify-center gap-2 whitespace-nowrap rounded-md text-sm font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 active:brightness-95 disabled:pointer-events-none disabled:opacity-50 aria-disabled:cursor-not-allowed aria-disabled:opacity-50 aria-disabled:active:brightness-100",
  {
    variants: {
      variant: {
        default: "bg-primary text-primary-foreground hover:bg-primary-hover",
        destructive: "bg-destructive text-destructive-foreground hover:bg-destructive/90",
        outline: "border border-input bg-background hover:bg-accent hover:text-accent-foreground",
        secondary: "bg-secondary text-secondary-foreground hover:bg-secondary/80",
        ghost: "hover:bg-accent hover:text-accent-foreground",
        link: "text-link underline-offset-4 hover:underline",
      },
      size: {
        default: "h-10 px-4 py-2",
        sm: "h-9 rounded-md px-3",
        lg: "h-11 rounded-md px-8",
        icon: "h-10 w-10",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  }
);

export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  asChild?: boolean;
  /**
   * The action is running. The button stays focusable (so focus is not dropped to the page mid-action),
   * says it is busy, shows a spinner and ignores further clicks — so a double submit can't happen.
   */
  loading?: boolean;
  /**
   * Why this action can't be taken right now. The button stays focusable and the reason is its description:
   * shown on hover AND on keyboard focus, and read by a screen reader. A `title` alone is reachable by
   * neither keyboard nor touch, and a plain `disabled` button can't be focused at all, so it can't explain
   * itself. If no reason can be given, don't disable the control: leave it out, or let it refuse with a message.
   */
  disabledReason?: string | null;
}

export const Button = React.forwardRef<HTMLButtonElement, ButtonProps>(
  ({ className, variant, size, asChild = false, loading = false, disabledReason, onClick, children, ...props }, ref) => {
    const reasonId = React.useId();
    const Comp = asChild ? Slot : "button";
    const blocked = loading || !!disabledReason;

    function handleClick(event: React.MouseEvent<HTMLButtonElement>) {
      // Also stops the implicit submit a form sends through its submit button's click.
      if (blocked) {
        event.preventDefault();
        return;
      }
      onClick?.(event);
    }

    const button = (
      <Comp
        className={cn(buttonVariants({ variant, size, className }))}
        ref={ref}
        {...props}
        onClick={handleClick}
        // A reason or a running action REPLACES native `disabled`: with both, the native one wins and the
        // button can't be focused, so it couldn't explain itself (a gated Save on a clean form did exactly that).
        disabled={blocked ? undefined : props.disabled}
        aria-disabled={blocked ? true : props["aria-disabled"]}
        aria-busy={loading ? true : props["aria-busy"]}
        aria-describedby={[props["aria-describedby"], disabledReason ? reasonId : null].filter(Boolean).join(" ") || undefined}
      >
        {/* Slot takes exactly one child, so the spinner is only added to a real <button>. */}
        {loading && !asChild ? (
          <>
            <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
            {children}
          </>
        ) : (
          children
        )}
      </Comp>
    );

    if (!disabledReason) return button;

    return (
      // The wrapper positions the reason. A full-width button keeps its width through it.
      <span className={cn("group/reason relative max-w-full", /(^|\s)w-full(\s|$)/.test(className ?? "") ? "flex w-full" : "inline-flex")}>
        {button}
        <span
          id={reasonId}
          role="tooltip"
          className={cn(
            "pointer-events-none absolute left-1/2 top-full z-50 mt-2 -translate-x-1/2",
            "w-max max-w-[240px] whitespace-normal rounded-md border bg-popover px-3 py-1.5",
            "text-center text-xs font-normal text-popover-foreground shadow-md",
            "opacity-0 transition-opacity group-hover/reason:opacity-100 group-focus-within/reason:opacity-100",
          )}
        >
          {disabledReason}
        </span>
      </span>
    );
  }
);

Button.displayName = "Button";
