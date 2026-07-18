import { describe, it, expect, vi } from "vitest";
import { renderHook } from "@testing-library/react";
import { useEscapeKey } from "../useEscapeKey";

describe("useEscapeKey", () => {
  it("calls onEscape when Escape is pressed and enabled=true", () => {
    const onEscape = vi.fn();
    renderHook(() => useEscapeKey(true, onEscape));
    document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }));
    expect(onEscape).toHaveBeenCalledOnce();
  });

  it("does NOT call onEscape when enabled=false", () => {
    const onEscape = vi.fn();
    renderHook(() => useEscapeKey(false, onEscape));
    document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }));
    expect(onEscape).not.toHaveBeenCalled();
  });

  it("does NOT call onEscape for non-Escape keys", () => {
    const onEscape = vi.fn();
    renderHook(() => useEscapeKey(true, onEscape));
    document.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter" }));
    expect(onEscape).not.toHaveBeenCalled();
  });

  it("cleans up listener when disabled", () => {
    const onEscape = vi.fn();
    const { rerender } = renderHook(
      ({ enabled }) => useEscapeKey(enabled, onEscape),
      { initialProps: { enabled: true } },
    );
    rerender({ enabled: false });
    document.dispatchEvent(new KeyboardEvent("keydown", { key: "Escape" }));
    expect(onEscape).not.toHaveBeenCalled();
  });
});
