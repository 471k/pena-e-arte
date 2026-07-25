import { describe, it, expect, beforeEach } from "vitest";
import { renderHook, cleanup } from "@testing-library/react";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";

describe("useDocumentMeta", () => {
  const ORIGINAL_TITLE = document.title;

  beforeEach(() => {
    // Reset head state between tests
    document.title = ORIGINAL_TITLE;
    document.head.querySelectorAll("meta[name],meta[property],link[rel=canonical]")
      .forEach((el) => el.remove());
  });

  it("sets document.title", () => {
    renderHook(() => useDocumentMeta({ title: "Test Title", canonical: "https://example.com/" }));
    expect(document.title).toBe("Test Title");
    cleanup();
  });

  it("creates og:title meta tag", () => {
    renderHook(() =>
      useDocumentMeta({ title: "OG Title", canonical: "https://example.com/" }),
    );
    const meta = document.head.querySelector('meta[property="og:title"]');
    expect(meta?.getAttribute("content")).toBe("OG Title");
    cleanup();
  });

  it("creates og:description meta tag when description is provided", () => {
    renderHook(() =>
      useDocumentMeta({ title: "T", description: "Desc text", canonical: "https://example.com/" }),
    );
    const meta = document.head.querySelector('meta[property="og:description"]');
    expect(meta?.getAttribute("content")).toBe("Desc text");
    cleanup();
  });

  it("creates og:image meta tag when ogImage is provided", () => {
    renderHook(() =>
      useDocumentMeta({ title: "T", ogImage: "https://cdn.example.com/img.jpg", canonical: "https://example.com/" }),
    );
    const meta = document.head.querySelector('meta[property="og:image"]');
    expect(meta?.getAttribute("content")).toBe("https://cdn.example.com/img.jpg");
    cleanup();
  });

  it("skips og:image when ogImage is undefined", () => {
    renderHook(() =>
      useDocumentMeta({ title: "T", canonical: "https://example.com/" }),
    );
    expect(document.head.querySelector('meta[property="og:image"]')).toBeNull();
    cleanup();
  });

  it("creates canonical link tag", () => {
    renderHook(() =>
      useDocumentMeta({ title: "T", canonical: "https://tattooos.co/s/test" }),
    );
    const link = document.head.querySelector('link[rel="canonical"]');
    expect(link?.getAttribute("href")).toBe("https://tattooos.co/s/test");
    cleanup();
  });

  it("restores previous title on unmount", () => {
    document.title = "Previous Title";
    const { unmount } = renderHook(() =>
      useDocumentMeta({ title: "New Title", canonical: "https://example.com/" }),
    );
    expect(document.title).toBe("New Title");
    unmount();
    expect(document.title).toBe("Previous Title");
  });

  it("removes injected meta nodes on unmount", () => {
    const { unmount } = renderHook(() =>
      useDocumentMeta({
        title:       "T",
        description: "D",
        ogImage:     "https://cdn.example.com/img.jpg",
        canonical:   "https://example.com/",
      }),
    );
    expect(document.head.querySelector('meta[property="og:title"]')).not.toBeNull();
    unmount();
    expect(document.head.querySelector('meta[property="og:title"]')).toBeNull();
    expect(document.head.querySelector('meta[property="og:image"]')).toBeNull();
    expect(document.head.querySelector('link[rel="canonical"]')).toBeNull();
  });
});
