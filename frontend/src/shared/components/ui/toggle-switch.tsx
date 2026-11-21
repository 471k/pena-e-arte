import { cn } from "@/shared/utils/cn";

interface ToggleSwitchProps {
  checked:      boolean;
  onChange:     () => void;
  disabled?:    boolean;
  "aria-label": string;
}

export function ToggleSwitch({
  checked,
  onChange,
  disabled = false,
  "aria-label": ariaLabel,
}: ToggleSwitchProps) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={ariaLabel}
      onClick={onChange}
      disabled={disabled}
      className={cn(
        "relative inline-flex h-5 w-9 shrink-0 cursor-pointer items-center rounded-full",
        // S05/2.5.8: the visible track is 36x20; this invisible extension makes the pointer target 40x28, over
        // the 24x24 AA minimum, without changing how it looks. The insets are measured from the PADDING box, inside
        // the 2px border, so -1.5/-1 reach 4px/2px past the visible edge. Kept small enough not to reach a
        // neighbouring row's switch in dense lists (Studio hours stacks them).
        "before:absolute before:-inset-x-1 before:-inset-y-1.5 before:content-['']",
        "border-2 border-transparent transition-colors focus-visible:outline-none",
        "focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2",
        "disabled:pointer-events-none disabled:opacity-50",
        checked ? "bg-primary" : "bg-input",
      )}
    >
      <span
        className={cn(
          "pointer-events-none relative flex h-4 w-4 items-center justify-center",
          "rounded-full bg-background shadow-lg ring-0 transition-transform",
          checked ? "translate-x-4" : "translate-x-0",
        )}
      >
        {checked && (
          <svg
            viewBox="0 0 8 8"
            className="h-2 w-2 text-link"
            aria-hidden="true"
          >
            <path
              d="M1 4l2 2 4-4"
              stroke="currentColor"
              strokeWidth="1.5"
              strokeLinecap="round"
              strokeLinejoin="round"
              fill="none"
            />
          </svg>
        )}
      </span>
    </button>
  );
}
