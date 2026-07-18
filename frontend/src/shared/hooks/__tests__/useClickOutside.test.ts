import { describe, it, expect, vi } from "vitest";
import { renderHook } from "@testing-library/react";
import { useClickOutside } from "../useClickOutside";

function makeRef(node: HTMLElement) {
  return { current: node };
}

describe("useClickOutside", () => {
  it("calls onClickOutside when mousedown fires outside the ref'd element and enabled=true", () => {
    const inside  = document.createElement("div");
    const outside = document.createElement("div");
    document.body.append(inside, outside);

    const onClickOutside = vi.fn();
    renderHook(() => useClickOutside(makeRef(inside), true, onClickOutside));

    outside.dispatchEvent(new MouseEvent("mousedown", { bubbles: true }));
    expect(onClickOutside).toHaveBeenCalledOnce();

    inside.remove();
    outside.remove();
  });

  it("does NOT call onClickOutside when mousedown fires inside the ref'd element", () => {
    const inside = document.createElement("div");
    document.body.append(inside);

    const onClickOutside = vi.fn();
    renderHook(() => useClickOutside(makeRef(inside), true, onClickOutside));

    inside.dispatchEvent(new MouseEvent("mousedown", { bubbles: true }));
    expect(onClickOutside).not.toHaveBeenCalled();

    inside.remove();
  });

  it("does NOT call onClickOutside when enabled=false", () => {
    const inside  = document.createElement("div");
    const outside = document.createElement("div");
    document.body.append(inside, outside);

    const onClickOutside = vi.fn();
    renderHook(() => useClickOutside(makeRef(inside), false, onClickOutside));

    outside.dispatchEvent(new MouseEvent("mousedown", { bubbles: true }));
    expect(onClickOutside).not.toHaveBeenCalled();

    inside.remove();
    outside.remove();
  });

  it("cleans up listener when disabled", () => {
    const inside  = document.createElement("div");
    const outside = document.createElement("div");
    document.body.append(inside, outside);

    const onClickOutside = vi.fn();
    const { rerender } = renderHook(
      ({ enabled }) => useClickOutside(makeRef(inside), enabled, onClickOutside),
      { initialProps: { enabled: true } },
    );
    rerender({ enabled: false });

    outside.dispatchEvent(new MouseEvent("mousedown", { bubbles: true }));
    expect(onClickOutside).not.toHaveBeenCalled();

    inside.remove();
    outside.remove();
  });
});
