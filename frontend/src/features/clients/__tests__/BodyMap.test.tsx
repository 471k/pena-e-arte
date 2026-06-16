import { describe, it, expect, vi } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach } from "vitest";

import { BodyMap, ALL_BODY_ZONES, FRONT_ZONES, BACK_ZONES } from "@/features/clients/components/BodyMap";

afterEach(() => cleanup());

describe("BodyMap", () => {
  it("renders all front zones by default", () => {
    render(<BodyMap locations={[]} onChange={vi.fn()} />);
    expect(screen.getByLabelText("Chest")).toBeInTheDocument();
    expect(screen.queryByLabelText("Upper Back")).not.toBeInTheDocument();
  });

  it("switching to 'Back' shows back zones instead of front zones", async () => {
    const user = userEvent.setup();
    render(<BodyMap locations={[]} onChange={vi.fn()} />);
    await user.click(screen.getByRole("button", { name: "Back" }));
    expect(screen.getByLabelText("Upper Back")).toBeInTheDocument();
    expect(screen.queryByLabelText("Chest")).not.toBeInTheDocument();
  });

  it("shows 'Click a zone to mark it.' when nothing is selected and not readOnly", () => {
    render(<BodyMap locations={[]} onChange={vi.fn()} />);
    expect(screen.getByText("Click a zone to mark it.")).toBeInTheDocument();
  });

  it("shows 'No body areas recorded.' when readOnly and nothing selected", () => {
    render(<BodyMap locations={[]} readOnly />);
    expect(screen.getByText("No body areas recorded.")).toBeInTheDocument();
  });

  it("renders chips for currently-selected locations", () => {
    render(<BodyMap locations={["left_shoulder", "left_forearm"]} readOnly />);
    expect(screen.getAllByText("Left Shoulder").length).toBeGreaterThanOrEqual(2);
    expect(screen.getAllByText("Left Forearm").length).toBeGreaterThanOrEqual(2);
    expect(screen.queryByText("No body areas recorded.")).not.toBeInTheDocument();
  });

  it("clicking an unselected zone calls onChange with the zone added", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<BodyMap locations={[]} onChange={onChange} />);
    await user.click(screen.getByLabelText("Chest"));
    expect(onChange).toHaveBeenCalledWith(["chest"]);
  });

  it("clicking an already-selected zone calls onChange with the zone removed", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<BodyMap locations={["chest"]} onChange={onChange} />);
    await user.click(screen.getByLabelText("Chest"));
    expect(onChange).toHaveBeenCalledWith([]);
  });

  it("clicking a zone in readOnly mode does not call onChange", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<BodyMap locations={[]} readOnly onChange={onChange} />);
    await user.click(screen.getByLabelText("Chest"));
    expect(onChange).not.toHaveBeenCalled();
  });

  it("selected zones get aria-pressed=true, unselected get aria-pressed=false", () => {
    render(<BodyMap locations={["chest"]} onChange={vi.fn()} />);
    expect(screen.getByLabelText("Chest")).toHaveAttribute("aria-pressed", "true");
    expect(screen.getByLabelText("Neck")).toHaveAttribute("aria-pressed", "false");
  });

  it("readOnly zones have no role='button'", () => {
    render(<BodyMap locations={[]} readOnly />);
    expect(screen.getByLabelText("Chest")).not.toHaveAttribute("role", "button");
  });

  it("non-readOnly zones have role='button'", () => {
    render(<BodyMap locations={[]} onChange={vi.fn()} />);
    expect(screen.getByLabelText("Chest")).toHaveAttribute("role", "button");
  });

  it("ALL_BODY_ZONES is the concatenation of FRONT_ZONES and BACK_ZONES", () => {
    expect(ALL_BODY_ZONES).toHaveLength(FRONT_ZONES.length + BACK_ZONES.length);
  });

  it("clicking a zone with no onChange handler does not throw", async () => {
    const user = userEvent.setup();
    render(<BodyMap locations={[]} />);
    await user.click(screen.getByLabelText("Chest"));
    expect(screen.getByText("Click a zone to mark it.")).toBeInTheDocument();
  });
});
