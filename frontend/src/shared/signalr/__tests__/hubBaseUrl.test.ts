import { describe, it, expect } from "vitest";
import { hubBaseUrl } from "@/shared/signalr/hubBaseUrl";

describe("hubBaseUrl", () => {
  it("connects straight to the local backend only for a dev browser on this machine", () => {
    expect(hubBaseUrl("localhost", true)).toBe("http://localhost:5078");
  });

  it.each(["test.tattooos.co", "127.0.0.1", "192.168.0.159"])(
    "uses the relative (proxied) URL for a dev client reaching the app as %s",
    (host) => {
      expect(hubBaseUrl(host, true)).toBe("");
    },
  );

  it("is always relative in production builds", () => {
    expect(hubBaseUrl("localhost", false)).toBe("");
    expect(hubBaseUrl("app.tattooos.co", false)).toBe("");
  });
});
