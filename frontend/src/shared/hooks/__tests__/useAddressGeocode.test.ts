import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act, renderHook } from "@testing-library/react";

async function flushMicrotasks() {
  for (let i = 0; i < 5; i++) await Promise.resolve();
}
import { useAddressGeocode } from "../useAddressGeocode";

function jsonResponse(body: unknown) {
  return { json: () => Promise.resolve(body) };
}

const RESULT = [
  {
    lat: "41.15",
    lon: "-8.61",
    address: { city: "Porto", country: "Portugal" },
  },
];

describe("useAddressGeocode", () => {
  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    vi.useRealTimers();
  });

  it("fires exactly one request after the debounce window elapses with no further changes", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(RESULT));
    vi.stubGlobal("fetch", fetchMock);
    const onResolved = vi.fn();

    renderHook(
      ({ address }) => useAddressGeocode(address, onResolved),
      { initialProps: { address: "Rua Central 5" } },
    );

    await vi.advanceTimersByTimeAsync(700);

    expect(fetchMock).toHaveBeenCalledTimes(1);
    expect(onResolved).toHaveBeenCalledWith({
      lat: 41.15, lng: -8.61, city: "Porto", country: "Portugal",
    });
  });

  it("does not fire below minLength", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(RESULT));
    vi.stubGlobal("fetch", fetchMock);

    renderHook(() => useAddressGeocode("abc", vi.fn()));

    await vi.advanceTimersByTimeAsync(1000);

    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("drops a stale in-flight response when the address changed before it resolved", async () => {
    let resolveFirst!: (v: unknown) => void;
    let resolveSecond!: (v: unknown) => void;
    const fetchMock = vi.fn()
      .mockImplementationOnce(() => new Promise((res) => { resolveFirst = res; }))
      .mockImplementationOnce(() => new Promise((res) => { resolveSecond = res; }));
    vi.stubGlobal("fetch", fetchMock);
    const onResolved = vi.fn();

    const { rerender } = renderHook(
      ({ address }) => useAddressGeocode(address, onResolved),
      { initialProps: { address: "Rua Central 5" } },
    );
    await vi.advanceTimersByTimeAsync(700);

    rerender({ address: "Rua Central 5, second try" });
    await vi.advanceTimersByTimeAsync(700);

    expect(fetchMock).toHaveBeenCalledTimes(2);

    // Resolve out of order: the newer (second) request settles first, then the stale first one.
    resolveSecond(jsonResponse([{ lat: "2", lon: "2", address: { city: "Second", country: "X" } }]));
    await Promise.resolve();
    resolveFirst(jsonResponse([{ lat: "1", lon: "1", address: { city: "First", country: "X" } }]));
    await Promise.resolve();
    await Promise.resolve();

    expect(onResolved).toHaveBeenCalledTimes(1);
    expect(onResolved).toHaveBeenCalledWith({ lat: 2, lng: 2, city: "Second", country: "X" });
  });

  it("aborts the in-flight request on unmount", async () => {
    const abortSpy = vi.fn();
    const fetchMock = vi.fn().mockImplementation((_url, opts: { signal: AbortSignal }) => {
      opts.signal.addEventListener("abort", abortSpy);
      return new Promise(() => {});
    });
    vi.stubGlobal("fetch", fetchMock);

    const { unmount } = renderHook(() => useAddressGeocode("Rua Central 5", vi.fn()));
    await vi.advanceTimersByTimeAsync(700);

    unmount();

    expect(abortSpy).toHaveBeenCalledOnce();
  });

  it("transitions status idle -> loading -> success", async () => {
    let resolveFetch!: (v: unknown) => void;
    vi.stubGlobal("fetch", vi.fn().mockImplementation(() => new Promise((res) => { resolveFetch = res; })));

    const { result } = renderHook(() => useAddressGeocode("Rua Central 5", vi.fn()));
    expect(result.current.status).toBe("idle");

    await act(async () => {
      await vi.advanceTimersByTimeAsync(700);
    });
    expect(result.current.status).toBe("loading");

    await act(async () => {
      resolveFetch(jsonResponse(RESULT));
      await flushMicrotasks();
    });
    expect(result.current.status).toBe("success");
  });

  it("transitions status to error when no results are found", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse([])));

    const { result } = renderHook(() => useAddressGeocode("Rua Central 5", vi.fn()));
    await act(async () => {
      await vi.advanceTimersByTimeAsync(700);
      await flushMicrotasks();
    });

    expect(result.current.status).toBe("error");
  });
});
