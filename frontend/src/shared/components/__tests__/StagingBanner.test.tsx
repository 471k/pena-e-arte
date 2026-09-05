import { describe, it, expect, afterEach, vi } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { StagingBanner } from "@/shared/components/StagingBanner";

describe("StagingBanner", () => {
  afterEach(() => {
    cleanup();
    vi.unstubAllEnvs();
  });

  it("renders when VITE_PUBLIC_URL points at the staging host", () => {
    vi.stubEnv("VITE_PUBLIC_URL", "https://staging.tattooos.co");
    render(<StagingBanner />);
    expect(screen.getByRole("alert")).toHaveTextContent(/staging/i);
  });

  it("does not render when VITE_PUBLIC_URL points at production", () => {
    vi.stubEnv("VITE_PUBLIC_URL", "https://app.tattooos.co");
    render(<StagingBanner />);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("does not render when VITE_PUBLIC_URL is unset", () => {
    vi.stubEnv("VITE_PUBLIC_URL", "");
    render(<StagingBanner />);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("dismisses on close and does not reappear within the same render", async () => {
    vi.stubEnv("VITE_PUBLIC_URL", "https://staging.tattooos.co");
    const user = userEvent.setup();
    render(<StagingBanner />);

    await user.click(screen.getByRole("button", { name: /dismiss staging banner/i }));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
