import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { PasswordInput } from "@/shared/components/ui/password-input";

describe("PasswordInput", () => {
  it("renders as a password field by default with a show/hide toggle", () => {
    render(<PasswordInput aria-label="Password" />);
    expect(screen.getByLabelText("Password")).toHaveAttribute("type", "password");
    expect(screen.getByRole("button", { name: /show password/i })).toBeInTheDocument();
  });

  it("is reachable via Tab, immediately after the input, and toggles visibility with a dynamic aria-label", async () => {
    const user = userEvent.setup();
    render(
      <>
        <input aria-label="Before" />
        <PasswordInput aria-label="Password" />
        <input aria-label="After" />
      </>,
    );

    await user.tab();
    expect(screen.getByLabelText("Before")).toHaveFocus();

    await user.tab();
    expect(screen.getByLabelText("Password")).toHaveFocus();

    await user.tab();
    const toggle = screen.getByRole("button", { name: /show password/i });
    expect(toggle).toHaveFocus();

    await user.keyboard("{Enter}");
    expect(screen.getByRole("button", { name: /hide password/i })).toBeInTheDocument();
    expect(screen.getByLabelText("Password")).toHaveAttribute("type", "text");

    await user.tab();
    expect(screen.getByLabelText("After")).toHaveFocus();
  });

  it("toggles the input type and aria-label on click", async () => {
    const user = userEvent.setup();
    render(<PasswordInput aria-label="Password" />);

    const toggle = screen.getByRole("button", { name: /show password/i });
    await user.click(toggle);

    expect(screen.getByLabelText("Password")).toHaveAttribute("type", "text");
    expect(screen.getByRole("button", { name: /hide password/i })).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: /hide password/i }));
    expect(screen.getByLabelText("Password")).toHaveAttribute("type", "password");
    expect(screen.getByRole("button", { name: /show password/i })).toBeInTheDocument();
  });
});
