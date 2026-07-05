import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { ResourceEmptyState } from "@/shared/components/ResourceEmptyState";
import { Button } from "@/shared/components/ui/button";

describe("ResourceEmptyState", () => {
  it("renders the heading", () => {
    render(<ResourceEmptyState icon={<span />} heading="Nothing here" body="Try again." />);
    expect(screen.getByText("Nothing here")).toBeInTheDocument();
  });

  it("renders the body text", () => {
    render(<ResourceEmptyState icon={<span />} heading="H" body="Body text here." />);
    expect(screen.getByText("Body text here.")).toBeInTheDocument();
  });

  it("renders the action when provided", () => {
    render(
      <ResourceEmptyState
        icon={<span />}
        heading="H"
        body="B"
        action={<Button>Click me</Button>}
      />
    );
    expect(screen.getByRole("button", { name: /click me/i })).toBeInTheDocument();
  });

  it("renders nothing for the action slot when action is undefined", () => {
    render(<ResourceEmptyState icon={<span />} heading="H" body="B" />);
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("icon wrapper has aria-hidden so it does not pollute the accessible tree", () => {
    render(
      <ResourceEmptyState
        icon={<span role="img" aria-label="test icon" />}
        heading="H"
        body="B"
      />
    );
    // The outer div hides the icon from AT — only heading/body/action are meaningful
    const wrapper = screen.queryByRole("img", { name: /test icon/i });
    // aria-hidden="true" on the parent means the img is hidden from AT
    expect(wrapper?.closest('[aria-hidden="true"]')).not.toBeNull();
  });
});
