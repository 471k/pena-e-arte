import { useState } from "react";
import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, waitFor, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter, Routes, Route } from "react-router-dom";
import { OnboardingTour } from "../OnboardingTour";
import type { TourStep } from "../OnboardingTour";

afterEach(() => cleanup());

function renderTour(steps: TourStep[], overrides: Partial<{ onComplete: () => void; onSkip: () => void }> = {}) {
  const onComplete = overrides.onComplete ?? vi.fn();
  const onSkip = overrides.onSkip ?? vi.fn();
  render(
    <MemoryRouter initialEntries={["/start"]}>
      <Routes>
        <Route
          path="*"
          element={
            <>
              <button data-tour="target-a">Target A</button>
              <OnboardingTour steps={steps} onComplete={onComplete} onSkip={onSkip} />
            </>
          }
        />
      </Routes>
    </MemoryRouter>,
  );
  return { onComplete, onSkip };
}

describe("OnboardingTour", () => {
  it("positions the spotlight against the target element's bounding rect", async () => {
    // Mock the prototype before mount — the tour measures synchronously on its
    // first effect run, before a per-instance spy could be attached in time.
    const spy = vi.spyOn(Element.prototype, "getBoundingClientRect").mockReturnValue({
      top: 100, left: 50, right: 90, bottom: 120, width: 40, height: 20, x: 50, y: 100,
      toJSON: () => ({}),
    } as DOMRect);

    render(
      <MemoryRouter initialEntries={["/start"]}>
        <button data-tour="target-a">Target A</button>
        <OnboardingTour
          steps={[{ targetSelector: '[data-tour="target-a"]', title: "Step 1", body: "Body 1" }]}
          onComplete={vi.fn()}
          onSkip={vi.fn()}
        />
      </MemoryRouter>,
    );

    const spotlight = await screen.findByTestId("tour-spotlight", {}, { timeout: 3000 });
    expect(spotlight).toHaveStyle({ top: "94px", left: "44px", width: "52px", height: "32px" });
    expect(await screen.findByRole("dialog", { name: "Step 1" })).toBeInTheDocument();

    spy.mockRestore();
  });

  it("skips a step whose selector never resolves and advances to the next", async () => {
    const steps: TourStep[] = [
      { targetSelector: '[data-tour="missing"]', title: "Missing", body: "Never appears" },
      { targetSelector: '[data-tour="target-a"]', title: "Found", body: "This one exists" },
    ];
    renderTour(steps);

    expect(await screen.findByRole("dialog", { name: "Found" }, { timeout: 3000 })).toBeInTheDocument();
  }, 10000);

  it("calls onComplete when every step is unresolvable", async () => {
    const onComplete = vi.fn();
    renderTour(
      [{ targetSelector: '[data-tour="nope"]', title: "Nope", body: "Never appears" }],
      { onComplete },
    );

    await waitFor(() => expect(onComplete).toHaveBeenCalled(), { timeout: 3000 });
  }, 10000);

  it("calls onSkip when Escape is pressed", async () => {
    const user = userEvent.setup();
    const onSkip = vi.fn();
    renderTour([{ targetSelector: '[data-tour="target-a"]', title: "Step 1", body: "Body 1" }], { onSkip });

    await screen.findByRole("dialog", { name: "Step 1" });
    await user.keyboard("{Escape}");

    expect(onSkip).toHaveBeenCalled();
  });

  it("navigates to a step's route before measuring its target", async () => {
    render(
      <MemoryRouter initialEntries={["/start"]}>
        <Routes>
          <Route path="/start" element={<div>Start page</div>} />
          <Route
            path="/designs"
            element={
              <>
                <button data-tour="target-a">Target A</button>
                <div>Designs page</div>
              </>
            }
          />
        </Routes>
        <OnboardingTour
          steps={[{ targetSelector: '[data-tour="target-a"]', title: "Cross-route step", body: "Body", route: "/designs" }]}
          onComplete={vi.fn()}
          onSkip={vi.fn()}
        />
      </MemoryRouter>,
    );

    expect(await screen.findByText("Designs page", {}, { timeout: 3000 })).toBeInTheDocument();
  }, 10000);

  it("calls onBeforeStep once per step, before the target needs to resolve — e.g. mounting a drawer's content on demand", async () => {
    const onBeforeStep = vi.fn();

    function DrawerHost() {
      const [drawerOpen, setDrawerOpen] = useState(false);
      return (
        <>
          {drawerOpen && <button data-tour="drawer-target">Drawer Target</button>}
          <OnboardingTour
            steps={[{ targetSelector: '[data-tour="drawer-target"]', title: "Drawer step", body: "Body" }]}
            onComplete={vi.fn()}
            onSkip={vi.fn()}
            onBeforeStep={(step) => {
              onBeforeStep(step);
              setDrawerOpen(true);
            }}
          />
        </>
      );
    }

    render(
      <MemoryRouter initialEntries={["/start"]}>
        <DrawerHost />
      </MemoryRouter>,
    );

    expect(await screen.findByRole("dialog", { name: "Drawer step" }, { timeout: 3000 })).toBeInTheDocument();
    expect(onBeforeStep).toHaveBeenCalledTimes(1);
    expect(onBeforeStep).toHaveBeenCalledWith(
      expect.objectContaining({ targetSelector: '[data-tour="drawer-target"]' }),
    );
  });

  it("advances to the next step and calls onComplete after the last step's Done button", async () => {
    const user = userEvent.setup();
    const onComplete = vi.fn();

    renderTour(
      [{ targetSelector: '[data-tour="target-a"]', title: "Only step", body: "Body" }],
      { onComplete },
    );

    await screen.findByRole("dialog", { name: "Only step" });
    await user.click(screen.getByRole("button", { name: /done/i }));

    expect(onComplete).toHaveBeenCalled();
  });
});
