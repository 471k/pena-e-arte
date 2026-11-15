import { describe, it, expect, vi, afterEach } from "vitest";
import { useState } from "react";
import { render, screen, cleanup, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import {
  BodyMap, ALL_BODY_ZONES, FRONT_ZONES, BACK_ZONES, bodyZoneLabel,
} from "@/features/clients/components/BodyMap";

afterEach(() => cleanup());

function figureZone(id: string): Element {
  const el = screen.getByTestId("body-map-figure").querySelector(`[data-zone="${id}"]`);
  if (!el) throw new Error(`zone ${id} not drawn`);
  return el;
}

// The real parent shape: BodyMap is controlled, so keyboard tests need state to see a tick stick.
function Controlled({ initial = [] as string[], onChange = vi.fn() as (l: string[]) => void }) {
  const [locations, setLocations] = useState<string[]>(initial);
  return (
    <BodyMap
      locations={locations}
      legend="Desired placement"
      onChange={(next) => { setLocations(next); onChange(next); }}
    />
  );
}

describe("BodyMap — editable (C10)", () => {
  it("is a named group with Front and Back sub-groups of labelled checkboxes, one per zone", () => {
    render(<BodyMap locations={[]} legend="Desired placement" onChange={vi.fn()} />);
    const group = screen.getByRole("group", { name: "Desired placement" });
    const front = within(group).getByRole("group", { name: "Front of body" });
    const back  = within(group).getByRole("group", { name: "Back of body" });
    expect(within(front).getAllByRole("checkbox")).toHaveLength(FRONT_ZONES.length);
    expect(within(back).getAllByRole("checkbox")).toHaveLength(BACK_ZONES.length);
  });

  it("each checkbox's name says the side, so front and back 'Left Shoulder' are distinguishable", () => {
    render(<BodyMap locations={[]} onChange={vi.fn()} />);
    expect(screen.getByRole("checkbox", { name: "Left Shoulder, front" })).toBeInTheDocument();
    expect(screen.getByRole("checkbox", { name: "Left Shoulder, back" })).toBeInTheDocument();
    // "(Back)" in a stored label isn't repeated inside the Back group.
    expect(screen.getByRole("checkbox", { name: "Left Thigh, back" })).toBeInTheDocument();
  });

  it("states whose left and right is meant", () => {
    const { rerender } = render(<BodyMap locations={[]} onChange={vi.fn()} perspective="self" />);
    expect(screen.getByRole("group", { name: "Body areas" })).toHaveAccessibleDescription(/your own left and right/);
    rerender(<BodyMap locations={[]} onChange={vi.fn()} perspective="client" />);
    expect(screen.getByRole("group", { name: "Body areas" })).toHaveAccessibleDescription(/the client's own left and right/);
  });

  it("every placement can be selected and removed with the keyboard alone", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<Controlled onChange={onChange} />);
    const chest = screen.getByRole("checkbox", { name: "Chest, front" });
    chest.focus();
    await user.keyboard(" ");
    expect(chest).toBeChecked();
    expect(onChange).toHaveBeenLastCalledWith(["chest"]);
    await user.keyboard(" ");
    expect(chest).not.toBeChecked();
    expect(onChange).toHaveBeenLastCalledWith([]);
  });

  it("Tab reaches the checkboxes; the figure itself adds no tab stops", async () => {
    const user = userEvent.setup();
    render(<Controlled />);
    // Front/Back view toggles first, then the first front checkbox.
    await user.tab();
    expect(screen.getByRole("button", { name: "Front of the figure" })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole("button", { name: "Back of the figure" })).toHaveFocus();
    await user.tab();
    expect(screen.getByRole("checkbox", { name: "Head, front" })).toHaveFocus();
    const figure = screen.getByTestId("body-map-figure");
    expect(figure).toHaveAttribute("aria-hidden", "true");
    expect(figure.querySelectorAll("[tabindex]")).toHaveLength(0);
  });

  it("map and list stay in sync: a click on the figure ticks the checkbox", async () => {
    const user = userEvent.setup();
    render(<Controlled />);
    await user.click(figureZone("chest"));
    expect(screen.getByRole("checkbox", { name: "Chest, front" })).toBeChecked();
    await user.click(figureZone("chest"));
    expect(screen.getByRole("checkbox", { name: "Chest, front" })).not.toBeChecked();
  });

  it("ticking a back area turns the figure to the back, highlighted", async () => {
    const user = userEvent.setup();
    render(<Controlled />);
    await user.click(screen.getByRole("checkbox", { name: "Upper Back, back" }));
    expect(screen.getByRole("button", { name: "Back of the figure" })).toHaveAttribute("aria-pressed", "true");
    expect(figureZone("upper_back")).toBeInTheDocument();
  });

  it("the selected summary names front and back in full", () => {
    render(<BodyMap locations={["left_shoulder", "left_shoulder_back"]} onChange={vi.fn()} />);
    const list = screen.getByRole("list", { name: "Selected areas" });
    expect(within(list).getByText("Left Shoulder · front")).toBeInTheDocument();
    expect(within(list).getByText("Left Shoulder · back")).toBeInTheDocument();
  });

  it("says nothing is selected yet", () => {
    render(<BodyMap locations={[]} onChange={vi.fn()} />);
    expect(screen.getByText("No areas selected yet.")).toBeInTheDocument();
  });

  it("legendHidden keeps the group's name for assistive tech", () => {
    render(<BodyMap locations={[]} onChange={vi.fn()} legend="Body map areas" legendHidden />);
    expect(screen.getByRole("group", { name: "Body map areas" })).toBeInTheDocument();
    expect(screen.getByText("Body map areas")).toHaveClass("sr-only");
  });
});

describe("BodyMap — read-only", () => {
  it("renders no checkboxes and no group, only the figure and the marked list", () => {
    render(<BodyMap locations={["left_shoulder", "left_forearm"]} readOnly />);
    expect(screen.queryAllByRole("checkbox")).toHaveLength(0);
    expect(screen.queryByRole("group")).not.toBeInTheDocument();
    const list = screen.getByRole("list", { name: "Marked body areas" });
    expect(within(list).getByText("Left Shoulder · front")).toBeInTheDocument();
    expect(within(list).getByText("Left Forearm · front")).toBeInTheDocument();
  });

  it("shows 'No body areas recorded.' when nothing is marked", () => {
    render(<BodyMap locations={[]} readOnly />);
    expect(screen.getByText("No body areas recorded.")).toBeInTheDocument();
  });

  it("a click on the figure in read-only mode does not call onChange", async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<BodyMap locations={[]} readOnly onChange={onChange} />);
    await user.click(figureZone("chest"));
    expect(onChange).not.toHaveBeenCalled();
  });

  it("with no onChange it is read-only and a click does not throw", async () => {
    const user = userEvent.setup();
    render(<BodyMap locations={[]} />);
    await user.click(figureZone("chest"));
    expect(screen.getByText("No body areas recorded.")).toBeInTheDocument();
  });

  it("the Back toggle shows the back of the figure", async () => {
    const user = userEvent.setup();
    render(<BodyMap locations={[]} readOnly />);
    await user.click(screen.getByRole("button", { name: "Back of the figure" }));
    expect(figureZone("upper_back")).toBeInTheDocument();
    expect(screen.getByTestId("body-map-figure").querySelector('[data-zone="chest"]')).toBeNull();
  });
});

describe("zones", () => {
  it("ALL_BODY_ZONES is the concatenation of FRONT_ZONES and BACK_ZONES", () => {
    expect(ALL_BODY_ZONES).toHaveLength(FRONT_ZONES.length + BACK_ZONES.length);
  });

  it("bodyZoneLabel gives every zone a unique full name", () => {
    const names = ALL_BODY_ZONES.map((z) => bodyZoneLabel(z.id));
    expect(new Set(names).size).toBe(ALL_BODY_ZONES.length);
    expect(bodyZoneLabel("nope")).toBeNull();
  });
});
