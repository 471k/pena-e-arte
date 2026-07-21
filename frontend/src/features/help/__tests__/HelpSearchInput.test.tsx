import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { HelpSearchInput } from "../components/HelpSearchInput";

describe("HelpSearchInput", () => {
  it("renders a search input with an accessible label", () => {
    render(<HelpSearchInput value="" onChange={vi.fn()} />);
    expect(screen.getByLabelText(/search help/i)).toBeInTheDocument();
  });

  it("calls onChange as the user types", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<HelpSearchInput value="" onChange={onChange} />);

    await user.type(screen.getByLabelText(/search help/i), "book");

    expect(onChange).toHaveBeenCalled();
  });

  it("does not show a clear button when value is empty", () => {
    render(<HelpSearchInput value="" onChange={vi.fn()} />);
    expect(screen.queryByLabelText(/clear search/i)).not.toBeInTheDocument();
  });

  it("shows a clear button when value is non-empty, and clears it on click", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<HelpSearchInput value="book" onChange={onChange} />);

    const clearButton = screen.getByLabelText(/clear search/i);
    await user.click(clearButton);

    expect(onChange).toHaveBeenCalledWith("");
  });
});
