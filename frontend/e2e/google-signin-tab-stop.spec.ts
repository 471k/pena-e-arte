import { test, expect, type Page } from "@playwright/test";
import { mockApiFallback } from "./helpers";

/**
 * Release gate, 25 Nov (slice 9, §3b): when Google refuses to render its sign-in button (an origin it does not
 * allow, or its service failing), it leaves a 0x0 iframe that is still a Tab stop. Our container used to stay
 * focusable until the 2.5 s render check hid it. This counts Tab stops, rather than reading the code: from the
 * moment the button is rendered until past the deadline, focus "Sign in", press Tab once and record whether focus
 * landed inside our container.
 *
 * Google's script is replaced by a stub so the run is deterministic and needs no network. The stub reproduces what
 * the real refusal state was measured to be (an iframe that is 0x0 and tabbable), and a "rendered" mode sizes it
 * at ~300 ms as production does. The rendered mode is the control: it must find the stop, which proves the probe
 * can see an iframe stop at all, so the refused mode's zero is not a blind spot.
 */

type Mode = "refused" | "rendered";

const STUB = `
(() => {
  const mode = window.__gsiMode;
  window.google = { accounts: { id: {
    initialize() {},
    prompt() {},
    disableAutoSelect() {},
    cancel() {},
    renderButton(container) {
      window.__gsiRenderAt = performance.now();
      const iframe = document.createElement("iframe");
      iframe.title = "Sign in with Google Button";
      iframe.style.cssText = "width:0;height:0;border:0;display:block";
      container.appendChild(iframe);
      if (mode === "rendered") {
        setTimeout(() => { iframe.style.width = "300px"; iframe.style.height = "44px"; }, 300);
      }
    },
  } } };
})();
`;

interface Sample { t: number; inContainer: boolean }

async function sampleTabStops(page: Page, mode: Mode): Promise<Sample[]> {
  await page.addInitScript((m: Mode) => {
    (window as unknown as { __gsiMode: Mode }).__gsiMode = m;
    localStorage.setItem("cookie-consent", "accepted");
  }, mode);
  await mockApiFallback(page);
  await page.route("https://accounts.google.com/gsi/client**", (route) =>
    route.fulfill({ status: 200, contentType: "application/javascript", body: STUB }),
  );

  await page.goto("/login");
  // Guard against a vacuous pass: if the stub had not loaded before the component mounted, the container would be
  // hidden from the start and there would be nothing to measure.
  await page.waitForFunction(() => typeof (window as unknown as { __gsiRenderAt?: number }).__gsiRenderAt === "number");

  const signIn = page.getByRole("button", { name: /^sign in$/i });
  const samples: Sample[] = [];
  for (;;) {
    await signIn.focus();
    await page.keyboard.press("Tab");
    const sample = await page.evaluate((): Sample => {
      const renderAt = (window as unknown as { __gsiRenderAt: number }).__gsiRenderAt;
      const container = document.querySelector("[data-testid='google-signin-button']");
      const active = document.activeElement;
      return {
        t: Math.round(performance.now() - renderAt),
        inContainer: !!container && !!active && container.contains(active),
      };
    });
    samples.push(sample);
    if (sample.t > 3500) break;
  }
  return samples;
}

test.describe("Google sign-in container: no Tab stop until Google has rendered", () => {
  test("refused: the container is never a Tab stop, including the 2.5 s before the render check", async ({ page }) => {
    const samples = await sampleTabStops(page, "refused");

    // Coverage: the window that used to leak is the first 2.5 s, so there must be samples early and across it.
    expect(samples[0].t, "first sample soon after renderButton").toBeLessThan(500);
    for (const from of [0, 500, 1000, 1500, 2000]) {
      expect(samples.some((s) => s.t >= from && s.t < from + 500), `a sample in ${from}-${from + 500} ms`).toBe(true);
    }

    const stops = samples.filter((s) => s.inContainer);
    expect(stops, `Tab stops inside the container (of ${samples.length} samples)`).toEqual([]);
  });

  // Production, 25 Nov: Google's script is `async`, and in 4 of 5 loads the component mounted before it arrived;
  // the old code then gave up for good. Here the script is HELD until the test releases it, after the component has
  // mounted, so the order is fixed by the test, not by how fast the dev server happens to be.
  test("late script: the button still renders, with no Tab stop while waiting", async ({ page }) => {
    await page.addInitScript(() => {
      (window as unknown as { __gsiMode: string }).__gsiMode = "rendered";
      localStorage.setItem("cookie-consent", "accepted");
    });
    await mockApiFallback(page);
    let release: () => void = () => undefined;
    const released = new Promise<void>((resolve) => { release = resolve; });
    await page.route("https://accounts.google.com/gsi/client**", async (route) => {
      await released;
      await route.fulfill({ status: 200, contentType: "application/javascript", body: STUB });
    });

    // "commit", not the default "load": the load event waits for async scripts, so it would wait on the held one.
    await page.goto("/login", { waitUntil: "commit" });
    const signIn = page.getByRole("button", { name: /^sign in$/i });
    await signIn.waitFor();
    const container = page.getByTestId("google-signin-button");

    // While the script is held back: mounted, waiting, and no stop.
    expect(await page.evaluate(() => typeof (window as unknown as { google?: unknown }).google)).toBe("undefined");
    await signIn.focus();
    await page.keyboard.press("Tab");
    expect(await container.evaluate((c) => c.contains(document.activeElement))).toBe(false);
    await expect(container).toHaveAttribute("data-status", "loading");

    // Release the script: it now certainly arrives after mount.
    release();

    // After it arrives: rendered, revealed and reachable.
    await expect(container).toHaveAttribute("data-status", "ready", { timeout: 5000 });
    await signIn.focus();
    await page.keyboard.press("Tab");
    expect(await container.evaluate((c) => c.contains(document.activeElement))).toBe(true);
  });

  test("rendered (control): once Google sizes its button, it is an ordinary Tab stop", async ({ page }) => {
    const samples = await sampleTabStops(page, "rendered");

    expect(samples.some((s) => s.inContainer), "the probe sees the button's stop").toBe(true);
    // And it is reachable within a second, not only after the 2.5 s deadline.
    const firstStop = samples.find((s) => s.inContainer);
    expect(firstStop?.t ?? Infinity).toBeLessThan(1000);
  });
});
