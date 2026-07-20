import { useEffect, useState } from "react";
import { useLocation, useNavigate } from "react-router-dom";
import { X } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { useEscapeKey } from "@/shared/hooks/useEscapeKey";

export interface TourStep {
  targetSelector: string;
  title: string;
  body: string;
  placement?: "top" | "bottom" | "left" | "right";
  route?: string;
}

export interface OnboardingTourProps {
  steps: TourStep[];
  onComplete: () => void;
  onSkip: () => void;
}

const MAX_POLL_ATTEMPTS = 20;
const POLL_INTERVAL_MS = 50;
const SPOTLIGHT_PADDING = 6;

export function OnboardingTour({ steps, onComplete, onSkip }: OnboardingTourProps) {
  const [stepIndex, setStepIndex] = useState(0);
  const [targetRect, setTargetRect] = useState<DOMRect | null>(null);
  const navigate = useNavigate();
  const location = useLocation();

  const step = steps[stepIndex] as TourStep | undefined;

  useEscapeKey(true, onSkip);

  // Resolve the current step's target: navigate if needed, then poll for the
  // element (route changes render asynchronously) up to ~1s before giving up.
  useEffect(() => {
    if (!step) return;
    let cancelled = false;
    let rafId1 = 0;
    let rafId2 = 0;
    let pollTimer: ReturnType<typeof setTimeout> | undefined;

    function measure(attempt: number) {
      if (cancelled) return;
      const el = document.querySelector(step!.targetSelector);
      if (!el) {
        if (attempt < MAX_POLL_ATTEMPTS) {
          pollTimer = setTimeout(() => measure(attempt + 1), POLL_INTERVAL_MS);
        } else {
          skipUnresolvableStep();
        }
        return;
      }
      setTargetRect(el.getBoundingClientRect());
    }

    // Clear the previous step's rect immediately so the spotlight doesn't briefly
    // show the wrong step while the new target is being located.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setTargetRect(null);

    if (step.route && location.pathname !== step.route) {
      navigate(step.route);
      rafId1 = requestAnimationFrame(() => {
        rafId2 = requestAnimationFrame(() => measure(0));
      });
    } else {
      measure(0);
    }

    function skipUnresolvableStep() {
      if (cancelled) return;
      goToStep(stepIndex + 1);
    }

    return () => {
      cancelled = true;
      cancelAnimationFrame(rafId1);
      cancelAnimationFrame(rafId2);
      if (pollTimer) clearTimeout(pollTimer);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [stepIndex]);

  // Recompute position on resize/scroll while a step's target is showing.
  useEffect(() => {
    if (!step || !targetRect) return;
    function recompute() {
      const el = document.querySelector(step!.targetSelector);
      if (el) setTargetRect(el.getBoundingClientRect());
    }
    window.addEventListener("resize", recompute);
    window.addEventListener("scroll", recompute, true);
    const el = document.querySelector(step.targetSelector);
    const observer = new ResizeObserver(recompute);
    if (el) observer.observe(el);
    return () => {
      window.removeEventListener("resize", recompute);
      window.removeEventListener("scroll", recompute, true);
      observer.disconnect();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [stepIndex, !!targetRect]);

  function goToStep(nextIndex: number) {
    if (nextIndex >= steps.length) {
      onComplete();
      return;
    }
    setStepIndex(nextIndex);
  }

  if (!step) return null;

  return (
    <div className="fixed inset-0 z-[1200]">
      {targetRect && (
        <>
          <div
            aria-hidden="true"
            data-testid="tour-spotlight"
            className="fixed rounded-md pointer-events-none transition-all duration-150"
            style={{
              top:    targetRect.top - SPOTLIGHT_PADDING,
              left:   targetRect.left - SPOTLIGHT_PADDING,
              width:  targetRect.width + SPOTLIGHT_PADDING * 2,
              height: targetRect.height + SPOTLIGHT_PADDING * 2,
              boxShadow: "0 0 0 9999px rgba(0,0,0,0.6)",
            }}
          />
          <TourPopover
            step={step}
            targetRect={targetRect}
            stepIndex={stepIndex}
            totalSteps={steps.length}
            onBack={() => goToStep(stepIndex - 1)}
            onNext={() => goToStep(stepIndex + 1)}
            onSkip={onSkip}
          />
        </>
      )}
    </div>
  );
}

interface TourPopoverProps {
  step: TourStep;
  targetRect: DOMRect;
  stepIndex: number;
  totalSteps: number;
  onBack: () => void;
  onNext: () => void;
  onSkip: () => void;
}

function TourPopover({ step, targetRect, stepIndex, totalSteps, onBack, onNext, onSkip }: TourPopoverProps) {
  const placement = step.placement ?? "bottom";
  const GAP = 12;
  const POPOVER_WIDTH = 300;

  const style: React.CSSProperties = { position: "fixed", width: POPOVER_WIDTH };

  switch (placement) {
    case "top":
      style.left   = clamp(targetRect.left, 8, window.innerWidth - POPOVER_WIDTH - 8);
      style.bottom = window.innerHeight - targetRect.top + GAP;
      break;
    case "left":
      style.top   = clamp(targetRect.top, 8, window.innerHeight - 8);
      style.right = window.innerWidth - targetRect.left + GAP;
      break;
    case "right":
      style.top  = clamp(targetRect.top, 8, window.innerHeight - 8);
      style.left = targetRect.right + GAP;
      break;
    case "bottom":
    default:
      style.left = clamp(targetRect.left, 8, window.innerWidth - POPOVER_WIDTH - 8);
      style.top  = targetRect.bottom + GAP;
      break;
  }

  return (
    <div
      role="dialog"
      aria-label={step.title}
      className="rounded-lg border bg-background shadow-lg p-4 space-y-3"
      style={style}
    >
      <div className="flex items-start justify-between gap-2">
        <h3 className="text-sm font-semibold">{step.title}</h3>
        <button
          type="button"
          onClick={onSkip}
          aria-label="Skip tour"
          className="text-muted-foreground hover:text-foreground shrink-0"
        >
          <X className="h-4 w-4" />
        </button>
      </div>
      <p className="text-sm text-muted-foreground">{step.body}</p>
      <div className="flex items-center justify-between pt-1">
        <span className="text-xs text-muted-foreground">{stepIndex + 1} / {totalSteps}</span>
        <div className="flex items-center gap-2">
          {stepIndex > 0 && (
            <Button type="button" variant="outline" size="sm" onClick={onBack}>Back</Button>
          )}
          <Button type="button" size="sm" onClick={onNext}>
            {stepIndex + 1 === totalSteps ? "Done" : "Next"}
          </Button>
        </div>
      </div>
    </div>
  );
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}
