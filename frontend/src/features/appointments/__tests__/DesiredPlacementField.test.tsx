import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { DesiredPlacementField } from "@/features/appointments/components/DesiredPlacementField";

afterEach(() => cleanup());

describe("DesiredPlacementField", () => {
  it("is a group named 'Desired placement' (C10: the old label pointed at an id nothing carried)", () => {
    render(<DesiredPlacementField locations={[]} onChange={vi.fn()} />);
    expect(screen.getByRole("group", { name: "Desired placement" })).toBeInTheDocument();
  });

  it("tells the person that left and right are their own", () => {
    render(<DesiredPlacementField locations={[]} onChange={vi.fn()} />);
    expect(screen.getByRole("group", { name: "Desired placement" }))
      .toHaveAccessibleDescription(/your own left and right/);
  });

  it("calls onChange when a placement is ticked", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<DesiredPlacementField locations={[]} onChange={onChange} />);

    await user.click(screen.getByRole("checkbox", { name: "Chest, front" }));

    expect(onChange).toHaveBeenCalledWith(["chest"]);
  });

  it("shows already-selected zones ticked and in the summary", () => {
    render(<DesiredPlacementField locations={["left_forearm"]} onChange={vi.fn()} />);
    expect(screen.getByRole("checkbox", { name: "Left Forearm, front" })).toBeChecked();
    expect(screen.getByText("Left Forearm · front")).toBeInTheDocument();
  });
});
