import { useState } from "react";
import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { PhoneInput } from "@/shared/components/ui/phone-input";
import { TimezoneSelect } from "@/shared/components/ui/timezone-select";
import { Select, SelectContent, SelectTrigger, SelectValue } from "@/shared/components/ui/select";
import { CountrySelectItems } from "@/shared/components/ui/country-select-items";
import { PHONE_COUNTRIES } from "@/shared/utils/phoneCountries";

// Keyboard, type-ahead and screen-reader behaviour of the long pickers, pinned explicitly.
// Their item lists are memoized so a re-render of the form around them does not rebuild every
// item. These are the paths that change would most plausibly break if it were done another way
// (rendering items only while open, for example), so they are asserted here rather than assumed.
// Run against the code before the memoization as well as after: same results.
//
// Two deliberate choices keep this file fast and deterministic under a loaded machine:
// - Type-ahead keys are dispatched synchronously (fireEvent.keyDown). Radix resets a type-ahead
//   search 1 s after the last key; with user.keyboard the keys can arrive further apart than that
//   when the CPU is busy, and "lisb" becomes four separate searches.
// - The open list is read through its role attributes (role="listbox"/"option", aria-selected)
//   with querySelectorAll. getAllByRole computes roles and visibility for every element, which over
//   400 options took longer than the test budget under load. The semantics asserted are the same:
//   these are the attributes a screen reader reads.
function typeAhead(el: Element, text: string) {
  for (const key of text) fireEvent.keyDown(el, { key });
}

async function openList(): Promise<HTMLElement> {
  await waitFor(() => expect(document.querySelector('[role="listbox"]')).not.toBeNull());
  return document.querySelector<HTMLElement>('[role="listbox"]')!;
}

function optionsOf(listbox: HTMLElement): HTMLElement[] {
  return Array.from(listbox.querySelectorAll<HTMLElement>('[role="option"]'));
}

function selectedOf(listbox: HTMLElement): HTMLElement[] {
  return optionsOf(listbox).filter((o) => o.getAttribute("aria-selected") === "true");
}


function ControlledTimezone({ onChange, initial }: { onChange: (v: string) => void; initial?: string }) {
  const [value, setValue] = useState<string | undefined>(initial);
  return (
    <>
      <label htmlFor="tz">Timezone</label>
      <TimezoneSelect id="tz" value={value} onChange={(v) => { setValue(v); onChange(v); }} />
    </>
  );
}

function ControlledCountry({ onChange }: { onChange: (v: string) => void }) {
  const [value, setValue] = useState("AL");
  return (
    <Select value={value} onValueChange={(v) => { setValue(v); onChange(v); }}>
      <SelectTrigger aria-label="Country"><SelectValue /></SelectTrigger>
      <SelectContent><CountrySelectItems /></SelectContent>
    </Select>
  );
}

describe("TimezoneSelect keyboard", () => {
  it("type-ahead on the closed trigger selects the first city starting with the typed letters", () => {
    const onChange = vi.fn();
    render(<ControlledTimezone onChange={onChange} />);
    const trigger = screen.getByRole("combobox", { name: "Timezone" });

    trigger.focus();
    typeAhead(trigger, "lisb");

    expect(onChange).toHaveBeenLastCalledWith("Europe/Lisbon");
    expect(trigger).toHaveTextContent(/Lisbon/);
    expect(document.querySelector('[role="listbox"]')).toBeNull();
  });

  it("opens from the keyboard with every zone as an option, the selected one marked and focused; type-ahead moves focus, Enter selects, focus returns", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<ControlledTimezone onChange={onChange} initial="Europe/Tirane" />);
    const trigger = screen.getByRole("combobox", { name: "Timezone" });

    trigger.focus();
    await user.keyboard("{Enter}");
    const listbox = await openList();

    expect(trigger).toHaveAttribute("aria-expanded", "true");
    expect(trigger).toHaveAttribute("aria-controls", listbox.closest("[id]")?.id ?? listbox.id);
    expect(optionsOf(listbox).length).toBeGreaterThan(300);
    const selected = selectedOf(listbox);
    expect(selected).toHaveLength(1);
    expect(selected[0]).toHaveTextContent(/Tirane/);
    await waitFor(() => expect(document.activeElement).toBe(selected[0]));

    // Inside the open list Radix moves focus to the match in a setTimeout, hence the wait.
    typeAhead(document.activeElement!, "lisb");
    await waitFor(() => expect(document.activeElement).toHaveTextContent(/Lisbon/));
    await user.keyboard("{Enter}");

    expect(onChange).toHaveBeenLastCalledWith("Europe/Lisbon");
    expect(document.querySelector('[role="listbox"]')).toBeNull();
    expect(trigger).toHaveFocus();
  });
});

describe("PhoneInput country code keyboard", () => {
  it("keeps its accessible name, opens from the keyboard with every calling code; arrows move, Enter picks, focus returns", async () => {
    const user = userEvent.setup();
    render(<PhoneInput value="" onChange={vi.fn()} />);
    const trigger = screen.getByRole("combobox", { name: "Country code" });
    const albania = PHONE_COUNTRIES.findIndex((c) => c.code === "AL");
    const next = PHONE_COUNTRIES[albania + 1];

    trigger.focus();
    await user.keyboard("{ArrowDown}");
    const listbox = await openList();

    expect(optionsOf(listbox)).toHaveLength(PHONE_COUNTRIES.length);
    const selected = selectedOf(listbox);
    expect(selected).toHaveLength(1);
    expect(selected[0]).toHaveTextContent("Albania (+355)");
    await waitFor(() => expect(document.activeElement).toBe(selected[0]));

    await user.keyboard("{ArrowDown}{Enter}");

    expect(document.querySelector('[role="listbox"]')).toBeNull();
    expect(trigger).toHaveTextContent(`+${next.callingCode}`);
    expect(trigger).toHaveFocus();
  });

  it("Escape closes without changing the code and returns focus to the trigger", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<PhoneInput value="" onChange={onChange} />);
    const trigger = screen.getByRole("combobox", { name: "Country code" });

    trigger.focus();
    await user.keyboard("{ArrowDown}");
    await openList();
    await user.keyboard("{ArrowDown}{ArrowDown}{Escape}");

    expect(document.querySelector('[role="listbox"]')).toBeNull();
    expect(onChange).not.toHaveBeenCalled();
    expect(trigger).toHaveTextContent("+355");
    expect(trigger).toHaveFocus();
  });
});

describe("type-ahead by country name in the flag-prefixed lists", () => {
  // Each item's visible text starts with a flag emoji, and Radix's type-ahead matches the start of
  // an item's text, so these lists set textValue to the country name. Before that, typing a name
  // never matched anything (2026-11-07).
  it("PhoneInput: typing a country name on the closed trigger picks its calling code", () => {
    const onChange = vi.fn();
    render(<PhoneInput value="" onChange={onChange} />);
    const trigger = screen.getByRole("combobox", { name: "Country code" });

    trigger.focus();
    typeAhead(trigger, "portu");

    expect(trigger).toHaveTextContent("+351");
    expect(document.querySelector('[role="listbox"]')).toBeNull();
  });

  it("PhoneInput: typing inside the open list moves focus to the country, Enter picks it", async () => {
    const user = userEvent.setup();
    render(<PhoneInput value="" onChange={vi.fn()} />);
    const trigger = screen.getByRole("combobox", { name: "Country code" });

    trigger.focus();
    await user.keyboard("{ArrowDown}");
    const listbox = await openList();
    await waitFor(() => expect(document.activeElement).toBe(selectedOf(listbox)[0]));
    typeAhead(document.activeElement!, "germ");
    await waitFor(() => expect(document.activeElement).toHaveTextContent("Germany (+49)"));
    await user.keyboard("{Enter}");

    expect(trigger).toHaveTextContent("+49");
    expect(trigger).toHaveFocus();
  });

  it("country select: typing a country name on the closed trigger selects it", () => {
    const onChange = vi.fn();
    render(<ControlledCountry onChange={onChange} />);
    const trigger = screen.getByRole("combobox", { name: "Country" });

    trigger.focus();
    typeAhead(trigger, "kos");

    expect(onChange).toHaveBeenLastCalledWith("XK");
    expect(trigger).toHaveTextContent("Kosovo");
  });
});

describe("country select keyboard", () => {
  it("shows the selected country's name on a bare SelectValue and selects with arrows + Enter", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<ControlledCountry onChange={onChange} />);
    const trigger = screen.getByRole("combobox", { name: "Country" });
    // A bare <SelectValue /> takes its text from the selected item, which Radix mounts even
    // while the list is closed. Rendering items only while open would leave this blank.
    expect(trigger).toHaveTextContent("Albania");

    const albania = PHONE_COUNTRIES.findIndex((c) => c.code === "AL");
    const next = PHONE_COUNTRIES[albania + 1];
    trigger.focus();
    await user.keyboard("{ArrowDown}");
    await openList();
    await user.keyboard("{ArrowDown}{Enter}");

    expect(onChange).toHaveBeenLastCalledWith(next.code);
    expect(trigger).toHaveTextContent(next.name);
  });
});
