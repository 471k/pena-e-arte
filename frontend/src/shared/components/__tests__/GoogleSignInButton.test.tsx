import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { act, render, screen } from "@testing-library/react";

import { GoogleSignInButton } from "@/shared/components/GoogleSignInButton";

describe("GoogleSignInButton", () => {
  const originalGoogle = window.google;

  beforeEach(() => {
    vi.useFakeTimers();
  });

  afterEach(() => {
    window.google = originalGoogle;
    vi.useRealTimers();
  });

  it("shows a fallback message when the Google SDK isn't loaded", () => {
    delete window.google;

    render(<GoogleSignInButton onCredential={vi.fn()} />);

    expect(screen.getByRole("alert")).toHaveTextContent(/unavailable right now/i);
  });

  it("hides the button container when the SDK isn't loaded", () => {
    delete window.google;

    render(<GoogleSignInButton onCredential={vi.fn()} />);

    expect(screen.getByTestId("google-signin-button")).toHaveClass("hidden");
  });

  describe("when the SDK is loaded", () => {
    const initialize   = vi.fn();
    const renderButton = vi.fn((container: HTMLElement) => {
      const iframe = document.createElement("iframe");
      Object.defineProperty(iframe, "clientWidth",  { value: 300, configurable: true });
      Object.defineProperty(iframe, "clientHeight", { value: 40,  configurable: true });
      container.appendChild(iframe);
    });

    beforeEach(() => {
      initialize.mockClear();
      renderButton.mockClear();
      window.google = {
        accounts: {
          id: {
            initialize,
            renderButton,
            prompt:             vi.fn(),
            disableAutoSelect:  vi.fn(),
            cancel:             vi.fn(),
          },
        },
      };
    });

    it("does not show the fallback message once the iframe renders", async () => {
      render(<GoogleSignInButton onCredential={vi.fn()} />);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(2500);
      });

      expect(screen.getByTestId("google-signin-button")).not.toHaveClass("hidden");
      expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    });

    it("shows the fallback message when the iframe never renders (0x0)", async () => {
      renderButton.mockImplementation((container: HTMLElement) => {
        container.appendChild(document.createElement("iframe"));
      });

      render(<GoogleSignInButton onCredential={vi.fn()} />);

      await act(async () => {
        await vi.advanceTimersByTimeAsync(2500);
      });

      expect(screen.getByRole("alert")).toHaveTextContent(/unavailable right now/i);
    });

    it("initializes with the credential callback wired to onCredential", () => {
      const onCredential = vi.fn();
      render(<GoogleSignInButton onCredential={onCredential} />);

      const config = initialize.mock.calls[0][0] as { callback: (r: { credential: string }) => void };
      config.callback({ credential: "test-credential" });

      expect(onCredential).toHaveBeenCalledWith("test-credential");
    });
  });
});
