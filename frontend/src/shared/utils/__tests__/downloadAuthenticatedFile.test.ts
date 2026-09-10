import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { downloadAuthenticatedFile } from "../downloadAuthenticatedFile";

// ── Mock URL.createObjectURL/revokeObjectURL — not available in jsdom ──────────
const MOCK_BLOB_URL = "blob:http://localhost/mock-export";
URL.createObjectURL = vi.fn().mockReturnValue(MOCK_BLOB_URL);
URL.revokeObjectURL = vi.fn();

describe("downloadAuthenticatedFile", () => {
  function spyOnClick() {
    return vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => {});
  }
  let clickSpy: ReturnType<typeof spyOnClick>;

  beforeEach(() => {
    clickSpy = spyOnClick();
    vi.mocked(URL.createObjectURL).mockClear();
    vi.mocked(URL.revokeObjectURL).mockClear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("attaches Authorization and X-Tenant-Id headers when both are present", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      blob: () => Promise.resolve(new Blob(["a,b\n1,2"], { type: "text/csv" })),
    });
    vi.stubGlobal("fetch", fetchMock);

    await downloadAuthenticatedFile("clients/export.csv", "clients.csv", "tok123", "tenant-1");

    expect(fetchMock).toHaveBeenCalledWith("/api/v1/clients/export.csv", {
      headers: { Authorization: "Bearer tok123", "X-Tenant-Id": "tenant-1" },
    });
  });

  it("omits headers that are null", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      blob: () => Promise.resolve(new Blob([""], { type: "text/csv" })),
    });
    vi.stubGlobal("fetch", fetchMock);

    await downloadAuthenticatedFile("clients/export.csv", "clients.csv", null, null);

    expect(fetchMock).toHaveBeenCalledWith("/api/v1/clients/export.csv", { headers: {} });
  });

  it("throws when the response is not OK", async () => {
    const fetchMock = vi.fn().mockResolvedValue({ ok: false, status: 403 });
    vi.stubGlobal("fetch", fetchMock);

    await expect(
      downloadAuthenticatedFile("clients/export.csv", "clients.csv", "tok", "tenant"),
    ).rejects.toThrow("Download failed (403)");
  });

  it("creates a blob URL, clicks a download anchor, and revokes the URL on success", async () => {
    const fetchMock = vi.fn().mockResolvedValue({
      ok: true,
      blob: () => Promise.resolve(new Blob(["a,b\n1,2"], { type: "text/csv" })),
    });
    vi.stubGlobal("fetch", fetchMock);

    await downloadAuthenticatedFile("clients/export.csv", "clients.csv", "tok", "tenant");

    expect(URL.createObjectURL).toHaveBeenCalledTimes(1);
    expect(clickSpy).toHaveBeenCalledTimes(1);
    expect(URL.revokeObjectURL).toHaveBeenCalledWith(MOCK_BLOB_URL);
  });
});
