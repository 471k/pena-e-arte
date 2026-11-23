import { describe, it, expect, afterEach, vi } from "vitest";
import { act, render, screen } from "@testing-library/react";
import { useHeaderFold } from "../useHeaderFold";

// jsdom has no layout, so the header's width and the root font size are set by hand. Real widths are covered by
// e2e/mobile-header.spec.ts at 390px, 100% and 200% text.
function Probe({ belowRem }: { belowRem: number }) {
  const { ref, folded } = useHeaderFold<HTMLDivElement>(belowRem);
  return <div ref={ref} data-testid="header">{folded ? "folded" : "inline"}</div>;
}

let observers: Array<() => void> = [];
class FakeResizeObserver {
  constructor(cb: () => void) { observers.push(cb); }
  observe() {}
  disconnect() {}
}

function setLayout(headerPx: number, rootPx: number) {
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue({ width: headerPx } as DOMRect);
  document.documentElement.style.fontSize = `${rootPx}px`;
}

afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  document.documentElement.style.fontSize = "";
  observers = [];
});

describe("useHeaderFold", () => {
  it("measures in rem: a 390px header folds at 200% text (12.2rem) but not at 100% (24.4rem)", () => {
    setLayout(390, 16);
    const { unmount } = render(<Probe belowRem={24} />);
    expect(screen.getByTestId("header")).toHaveTextContent("inline");
    unmount();

    setLayout(390, 32);
    render(<Probe belowRem={24} />);
    expect(screen.getByTestId("header")).toHaveTextContent("folded");
  });

  it("a header with no width (no layout yet, or jsdom) reads as not folded, never as narrow", () => {
    setLayout(0, 16);
    render(<Probe belowRem={24} />);
    expect(screen.getByTestId("header")).toHaveTextContent("inline");
  });

  it("re-measures when the text size changes with the header's px width unchanged", () => {
    vi.stubGlobal("ResizeObserver", FakeResizeObserver);
    setLayout(390, 16);
    render(<Probe belowRem={24} />);
    expect(screen.getByTestId("header")).toHaveTextContent("inline");

    // The user raises the text size: the header is still 390px, only the 1rem probe resizes.
    document.documentElement.style.fontSize = "32px";
    act(() => { observers.forEach((fire) => fire()); });
    expect(screen.getByTestId("header")).toHaveTextContent("folded");
  });
});
