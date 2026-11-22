import { describe, it, expect, vi, beforeAll } from "vitest";
import { act, renderHook } from "@testing-library/react";
import { useCategorizedImageUpload } from "@/shared/hooks/useCategorizedImageUpload";

beforeAll(() => {
  URL.createObjectURL = vi.fn(() => "blob:preview");
  URL.revokeObjectURL = vi.fn();
});

function fileList(...files: File[]): FileList {
  return Object.assign(files, { item: (i: number) => files[i] ?? null }) as unknown as FileList;
}

describe("useCategorizedImageUpload — C06: a failed upload can be retried with the same file", () => {
  it("retries the same File and becomes done when the second attempt works", async () => {
    const photo = new File(["x"], "area.png", { type: "image/png" });
    const upload = vi.fn<(f: File) => Promise<string | null>>()
      .mockResolvedValueOnce(null)
      .mockResolvedValueOnce("https://media.example.com/area.png");
    const { result } = renderHook(() => useCategorizedImageUpload({ maxImages: 6, upload }));

    await act(() => result.current.pick(fileList(photo)));
    expect(result.current.images[0].status).toBe("error");

    await act(() => result.current.retry(result.current.images[0].id));
    expect(upload).toHaveBeenCalledTimes(2);
    expect(upload.mock.calls[1][0]).toBe(photo);
    expect(result.current.images[0].status).toBe("done");
    expect(result.current.doneUrls()).toEqual(["https://media.example.com/area.png"]);
  });

  it("does nothing for an image that was removed", async () => {
    const upload = vi.fn<(f: File) => Promise<string | null>>().mockResolvedValue(null);
    const { result } = renderHook(() => useCategorizedImageUpload({ maxImages: 6, upload }));
    await act(() => result.current.pick(fileList(new File(["x"], "a.png", { type: "image/png" }))));
    const id = result.current.images[0].id;
    act(() => result.current.remove(id));
    await act(() => result.current.retry(id));
    expect(upload).toHaveBeenCalledTimes(1);
  });
});
