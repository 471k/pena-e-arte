import { describe, it, expect } from "vitest";
import { renderHook } from "@testing-library/react";
import { useResultCountAnnouncement } from "@/shared/hooks/useResultCountAnnouncement";

type Props = { queryKey: string; count: number; settled: boolean; hasMore?: boolean };
const noun = { one: "client", many: "clients" };

function setup(initial: Props) {
  return renderHook((p: Props) => useResultCountAnnouncement({ ...p, noun }), { initialProps: initial });
}

describe("useResultCountAnnouncement (S06: only a change the person caused speaks)", () => {
  it("says nothing on the first load", () => {
    const { result } = setup({ queryKey: "", count: 12, settled: true });
    expect(result.current).toBe("");
  });

  it("says nothing when the data refreshes on its own under the same query", () => {
    const { result, rerender } = setup({ queryKey: "", count: 12, settled: true });
    rerender({ queryKey: "", count: 13, settled: true });
    expect(result.current).toBe("");
  });

  it("after a search: silent while loading, then the count once settled", () => {
    const { result, rerender } = setup({ queryKey: "", count: 12, settled: true });
    rerender({ queryKey: "ann", count: 12, settled: false });
    expect(result.current).toBe("");
    rerender({ queryKey: "ann", count: 1, settled: true });
    expect(result.current).toBe("1 client found.");
  });

  it("names zero results and a paged count", () => {
    const { result, rerender } = setup({ queryKey: "", count: 3, settled: true });
    rerender({ queryKey: "zzz", count: 0, settled: true });
    expect(result.current).toBe("No clients found.");
    rerender({ queryKey: "a", count: 20, settled: true, hasMore: true });
    expect(result.current).toBe("20 clients shown. More can be loaded.");
  });

  it("goes back to the first query and still speaks (it is the person's change)", () => {
    const { result, rerender } = setup({ queryKey: "", count: 3, settled: true });
    rerender({ queryKey: "a", count: 1, settled: true });
    rerender({ queryKey: "", count: 3, settled: true });
    expect(result.current).toBe("3 clients found.");
  });

  it("says nothing when the load failed", () => {
    const { result, rerender } = setup({ queryKey: "", count: 3, settled: true });
    rerender({ queryKey: "a", count: 0, settled: false });
    expect(result.current).toBe("");
  });
});
