import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { DesiredPlacementField } from "@/features/appointments/components/DesiredPlacementField";

afterEach(() => cleanup());

describe("DesiredPlacementField", () => {
  it("renders the 'Desired placement' label", () => {
    render(<DesiredPlacementField locations={[]} onChange={vi.fn()} />);
    expect(screen.getByText("Desired placement")).toBeInTheDocument();
  });

  it("renders the underlying BodyMap picker", () => {
    render(<DesiredPlacementField locations={[]} onChange={vi.fn()} />);
    expect(screen.getByLabelText(/body map/i)).toBeInTheDocument();
  });

  it("calls onChange when a zone is clicked", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<DesiredPlacementField locations={[]} onChange={onChange} />);

    await user.click(screen.getByLabelText("Chest"));

    expect(onChange).toHaveBeenCalledWith(["chest"]);
  });

  it("shows already-selected zones as chips", () => {
    render(<DesiredPlacementField locations={["left_forearm"]} onChange={vi.fn()} />);
    expect(screen.getAllByText("Left Forearm").length).toBeGreaterThanOrEqual(1);
  });
});
