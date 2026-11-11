import { describe, it, expect } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useEditBuffer } from "../useEditBuffer";

interface Data { value: number }
const toForm = (d: Data) => ({ text: String(d.value) });

function setup(initial: { source: Data | undefined; key: string | undefined }) {
  return renderHook(({ source, key }) => useEditBuffer(source, key, toForm), { initialProps: initial });
}

describe("useEditBuffer", () => {
  it("stays empty until there is data and a key", () => {
    const { result, rerender } = setup({ source: undefined, key: undefined });
    expect(result.current[0]).toBeNull();
    rerender({ source: { value: 1 }, key: undefined });
    expect(result.current[0]).toBeNull();
    rerender({ source: { value: 1 }, key: "a" });
    expect(result.current[0]).toEqual({ text: "1" });
  });

  it("keeps an edit when a refetch brings a new object for the same thing", () => {
    const { result, rerender } = setup({ source: { value: 1 }, key: "a" });
    act(() => result.current[1]({ text: "typed" }));
    rerender({ source: { value: 2 }, key: "a" });
    expect(result.current[0]).toEqual({ text: "typed" });
  });

  it("refills when the thing being edited changes", () => {
    const { result, rerender } = setup({ source: { value: 1 }, key: "a" });
    act(() => result.current[1]({ text: "typed" }));
    rerender({ source: { value: 7 }, key: "b" });
    expect(result.current[0]).toEqual({ text: "7" });
  });

  it("refills the same thing afresh after the key was cleared (a sheet closed and reopened)", () => {
    const { result, rerender } = setup({ source: { value: 1 }, key: "a" });
    act(() => result.current[1]({ text: "typed" }));
    rerender({ source: { value: 1 }, key: undefined });
    rerender({ source: { value: 3 }, key: "a" });
    expect(result.current[0]).toEqual({ text: "3" });
  });

  it("reset fills from what the server returned", () => {
    const { result } = setup({ source: { value: 1 }, key: "a" });
    act(() => result.current[1]({ text: "typed" }));
    act(() => result.current[2]({ value: 9 }));
    expect(result.current[0]).toEqual({ text: "9" });
  });
});
