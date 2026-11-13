import { useState } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, cleanup, waitFor } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import { OnboardingTour, type TourStep } from "../OnboardingTour";

// E05c: a tour target inside a `hidden` subtree (a collapsed Studio Settings group) is in the DOM with no
// box. Measuring it spotlit a 12x12 square at (-6,-6) and nothing failed. Now it counts as not there yet.

afterEach(() => cleanup());

const STEPS: TourStep[] = [
  { targetSelector: '[data-tour="inside"]', title: "Hidden card", body: "b" },
  { targetSelector: '[data-tour="visible"]', title: "Visible card", body: "b" },
];

describe("OnboardingTour with a target inside a hidden group", () => {
  it("never spotlights a target that stays hidden: the step is skipped", async () => {
    render(
      <MemoryRouter>
        <div hidden><div data-tour="inside">card</div></div>
        <div data-tour="visible">other</div>
        <OnboardingTour steps={STEPS} onComplete={vi.fn()} onSkip={vi.fn()} />
      </MemoryRouter>,
    );
    expect(await screen.findByRole("dialog", { name: "Visible card" })).toBeInTheDocument();
    expect(screen.queryByRole("dialog", { name: "Hidden card" })).not.toBeInTheDocument();
  });

  it("waits for a group that opens before the step gives up, then shows the step", async () => {
    function OpensLater() {
      const [open, setOpen] = useState(false);
      return (
        <>
          <div hidden={!open}><div data-tour="inside">card</div></div>
          <div data-tour="visible">other</div>
          <OnboardingTour
            steps={STEPS}
            onComplete={vi.fn()}
            onSkip={vi.fn()}
            onBeforeStep={() => { setTimeout(() => setOpen(true), 150); }}
          />
        </>
      );
    }
    render(<MemoryRouter><OpensLater /></MemoryRouter>);
    expect(await screen.findByRole("dialog", { name: "Hidden card" })).toBeInTheDocument();
    await waitFor(() => expect(document.querySelector('[data-tour="inside"]')!.closest("[hidden]")).toBeNull());
  });
});
