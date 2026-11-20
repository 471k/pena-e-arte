import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FormErrorSummary } from "@/shared/components/FormErrorSummary";

const items = [
  { fieldId: "email", label: "Email", message: "Enter a valid email" },
  { fieldId: "images-AreaPhoto", label: "Area photo", message: "A photo of the area is required." },
];

function Form({ attempt, list = items }: { attempt: number; list?: typeof items }) {
  return (
    <form>
      <FormErrorSummary items={list} attempt={attempt} />
      <label htmlFor="email">Email</label>
      <input id="email" />
      <input id="images-AreaPhoto" type="file" className="sr-only" aria-label="Area photo" />
    </form>
  );
}

describe("FormErrorSummary (S04)", () => {
  it("renders nothing when there is nothing to fix", () => {
    render(<Form attempt={1} list={[]} />);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("lists every error with its field name and takes focus after a failed submit", () => {
    render(<Form attempt={1} />);
    const summary = screen.getByRole("alert");
    expect(summary).toHaveTextContent("There are 2 things to fix");
    expect(screen.getByRole("link", { name: "Email: Enter a valid email" })).toBeInTheDocument();
    expect(summary).toHaveFocus();
  });

  it("each link moves focus to its own field, including errors kept outside the form library", async () => {
    const user = userEvent.setup();
    render(<Form attempt={1} />);
    await user.click(screen.getByRole("link", { name: /area photo/i }));
    expect(document.getElementById("images-AreaPhoto")).toHaveFocus();
    await user.click(screen.getByRole("link", { name: /email/i }));
    expect(screen.getByLabelText("Email", { selector: "#email" })).toHaveFocus();
  });

  it("takes focus once per attempt, not on every re-render", () => {
    const { rerender } = render(<Form attempt={1} />);
    (document.getElementById("email") as HTMLInputElement).focus();
    rerender(<Form attempt={1} />);
    expect(document.getElementById("email")).toHaveFocus();
    rerender(<Form attempt={2} />);
    expect(screen.getByRole("alert")).toHaveFocus();
  });
});
