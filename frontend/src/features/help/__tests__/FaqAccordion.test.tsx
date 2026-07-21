import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { FaqAccordion } from "../components/FaqAccordion";
import type { FaqItem } from "../help.types";

const items: FaqItem[] = [
  { id: "f1", roles: ["client"], question: "How long is the trial?", answer: "14 days." },
  { id: "f2", roles: ["client"], question: "What is the grace period?", answer: "7 days." },
];

describe("FaqAccordion", () => {
  it("renders a message when there are no items", () => {
    render(<FaqAccordion items={[]} />);
    expect(screen.getByText(/no faq available/i)).toBeInTheDocument();
  });

  it("renders every question", () => {
    render(<FaqAccordion items={items} />);
    expect(screen.getByText(/how long is the trial/i)).toBeInTheDocument();
    expect(screen.getByText(/what is the grace period/i)).toBeInTheDocument();
  });

  it("expands to show the answer when a question is clicked", async () => {
    const user = userEvent.setup();
    render(<FaqAccordion items={items} />);

    expect(screen.queryByText(/14 days/)).not.toBeInTheDocument();
    await user.click(screen.getByText(/how long is the trial/i));
    expect(await screen.findByText(/14 days/)).toBeInTheDocument();
  });

  it("closes the previous item when a new one is opened (single-open accordion)", async () => {
    const user = userEvent.setup();
    render(<FaqAccordion items={items} />);

    await user.click(screen.getByText(/how long is the trial/i));
    expect(await screen.findByText(/14 days/)).toBeInTheDocument();

    await user.click(screen.getByText(/what is the grace period/i));
    expect(await screen.findByText(/7 days/)).toBeInTheDocument();
    expect(screen.queryByText(/14 days/)).not.toBeInTheDocument();
  });
});
