import { forwardRef, useState, type ComponentPropsWithoutRef, type ElementRef, type ReactNode } from "react";
import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { PhoneInput } from "@/shared/components/ui/phone-input";
import { TimezoneSelect } from "@/shared/components/ui/timezone-select";
import { Select, SelectContent, SelectTrigger, SelectValue } from "@/shared/components/ui/select";
import { CountrySelectItems } from "@/shared/components/ui/country-select-items";

// Regression guard for the pickers' render cost (2026-11-07). Radix Select keeps every item
// mounted while the list is closed, so a picker whose items are rebuilt on each render re-renders
// hundreds of them (plus a hidden native <option> each) on every keystroke in the form around it.
// The item lists are memoized; these tests count how often the SelectItem elements are rebuilt
// (the counting wrapper renders only when its parent hands it a new element) and fail if a parent
// re-render, or typing in the phone number, rebuilds them again. Radix's own item internals still
// re-render on a change of the Select itself, which the last test checks from the outside.
const itemRendered = vi.hoisted(() => vi.fn());
const itemRenders = (): number => itemRendered.mock.calls.length;

vi.mock("@/shared/components/ui/select", async (importOriginal) => {
  const actual = await importOriginal<typeof import("@/shared/components/ui/select")>();
  const CountingSelectItem = forwardRef<
    ElementRef<typeof actual.SelectItem>,
    ComponentPropsWithoutRef<typeof actual.SelectItem>
  >((props, ref) => {
    itemRendered();
    return <actual.SelectItem ref={ref} {...props} />;
  });
  return { ...actual, SelectItem: CountingSelectItem };
});

beforeEach(() => {
  itemRendered.mockClear();
});

// A parent that re-renders on demand without changing anything the picker shows, like a form
// re-rendering on a keystroke in another field.
function Parent({ children }: { children: (tick: number) => ReactNode }) {
  const [tick, setTick] = useState(0);
  return (
    <form>
      <button type="button" onClick={() => setTick((t) => t + 1)}>rerender</button>
      {children(tick)}
    </form>
  );
}

function ControlledPhone() {
  const [value, setValue] = useState("");
  return <PhoneInput value={value} onChange={setValue} />;
}

describe("picker item lists are not rebuilt on unrelated renders", () => {
  it("PhoneInput: parent re-renders and typing in the number leave the calling-code items alone", async () => {
    const user = userEvent.setup();
    render(<Parent>{() => <ControlledPhone />}</Parent>);
    const afterMount = itemRenders();
    expect(afterMount).toBeGreaterThan(200);

    for (let i = 0; i < 3; i++) fireEvent.click(screen.getByRole("button", { name: "rerender" }));
    await user.type(screen.getByRole("textbox"), "6923");

    expect(itemRenders()).toBe(afterMount);
  });

  it("TimezoneSelect: parent re-renders with a new onChange leave the zone items alone", () => {
    render(
      <Parent>
        {(tick) => <TimezoneSelect value="Europe/Tirane" countryCode="AL" onChange={() => tick} />}
      </Parent>,
    );
    const afterMount = itemRenders();
    expect(afterMount).toBeGreaterThan(300);

    for (let i = 0; i < 3; i++) fireEvent.click(screen.getByRole("button", { name: "rerender" }));

    expect(itemRenders()).toBe(afterMount);
  });

  it("TimezoneSelect: a saved zone outside both lists keeps its item stable too", () => {
    render(
      <Parent>
        {(tick) => <TimezoneSelect value="Etc/GMT-3" onChange={() => tick} />}
      </Parent>,
    );
    const afterMount = itemRenders();

    for (let i = 0; i < 3; i++) fireEvent.click(screen.getByRole("button", { name: "rerender" }));

    expect(itemRenders()).toBe(afterMount);
  });

  it("CountrySelectItems: parent re-renders leave the country items alone", () => {
    render(
      <Parent>
        {(tick) => (
          <Select value="AL" onValueChange={() => tick}>
            <SelectTrigger aria-label="Country"><SelectValue /></SelectTrigger>
            <SelectContent><CountrySelectItems /></SelectContent>
          </Select>
        )}
      </Parent>,
    );
    const afterMount = itemRenders();
    expect(afterMount).toBeGreaterThan(200);

    for (let i = 0; i < 3; i++) fireEvent.click(screen.getByRole("button", { name: "rerender" }));

    expect(itemRenders()).toBe(afterMount);
  });

  it("memoized items still follow the selection: the newly selected option is the one marked", async () => {
    const user = userEvent.setup();
    function Switchable() {
      const [value, setValue] = useState("AL");
      return (
        <>
          <button type="button" onClick={() => setValue("PT")}>pick PT</button>
          <Select value={value} onValueChange={setValue}>
            <SelectTrigger aria-label="Country"><SelectValue /></SelectTrigger>
            <SelectContent><CountrySelectItems /></SelectContent>
          </Select>
        </>
      );
    }
    render(<Switchable />);

    fireEvent.click(screen.getByRole("button", { name: "pick PT" }));
    const trigger = screen.getByRole("combobox", { name: "Country" });
    expect(trigger).toHaveTextContent("Portugal");
    trigger.focus();
    await user.keyboard("{ArrowDown}");
    await waitFor(() => expect(document.querySelector('[role="listbox"]')).not.toBeNull());

    const selected = Array.from(document.querySelectorAll('[role="option"][aria-selected="true"]'));
    expect(selected).toHaveLength(1);
    expect(selected[0]).toHaveTextContent("Portugal");
  });
});
