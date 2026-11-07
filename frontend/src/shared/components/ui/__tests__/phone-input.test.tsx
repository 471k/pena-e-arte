import { useState } from "react";
import { describe, it, expect, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { isValidPhoneNumber } from "libphonenumber-js/min";
import { PhoneInput } from "@/shared/components/ui/phone-input";

// A realistic harness: every real call site (CreateClientPage, StudioProfilePage,
// ReminderDialog) feeds the emitted value back into `value` on every change — PhoneInput's
// own resync guard (comparing `value` against what it last emitted) depends on that. A test
// that passes a static `value` with a non-feeding-back `onChange` does not match real usage
// and defeats that guard, so typing/interaction tests render through this wrapper instead.
function ControlledPhoneInput({ onChange }: { onChange: (v: string) => void }) {
  const [value, setValue] = useState("");
  return (
    <PhoneInput
      value={value}
      onChange={(v) => {
        setValue(v);
        onChange(v);
      }}
    />
  );
}

describe("PhoneInput", () => {
  it("renders with an empty value showing the default country (Albania) and an empty national input", () => {
    render(<PhoneInput value="" onChange={vi.fn()} />);
    expect(screen.getByRole("combobox", { name: "Country code" })).toHaveTextContent("+355");
    expect(screen.getByRole("textbox")).toHaveValue("");
  });

  it("preselects the country the caller passes (the studio's own) instead of the default", () => {
    render(<PhoneInput value="" onChange={vi.fn()} defaultCountry="PT" />);
    expect(screen.getByRole("combobox", { name: "Country code" })).toHaveTextContent("+351");
  });

  it("shows a placeholder example for the selected country, not a Portuguese one", () => {
    const { rerender } = render(<PhoneInput value="" onChange={vi.fn()} />);
    const albanian: string = screen.getByRole("textbox").getAttribute("placeholder") ?? "";
    rerender(<PhoneInput value="" onChange={vi.fn()} defaultCountry="PT" />);
    const portuguese: string = screen.getByRole("textbox").getAttribute("placeholder") ?? "";

    expect(albanian).not.toBe("");
    expect(portuguese).toBe("912 345 678");
    expect(albanian).not.toBe(portuguese);
  });

  it("follows a studio country that arrives after first render while the field is untouched", () => {
    const { rerender } = render(<PhoneInput value="" onChange={vi.fn()} />);
    expect(screen.getByRole("combobox", { name: "Country code" })).toHaveTextContent("+355");

    rerender(<PhoneInput value="" onChange={vi.fn()} defaultCountry="PT" />);

    expect(screen.getByRole("combobox", { name: "Country code" })).toHaveTextContent("+351");
  });

  it("never overrides what the user already typed when the studio country arrives late", async () => {
    const user = userEvent.setup();
    function Harness({ country }: { country?: "PT" }) {
      const [value, setValue] = useState("");
      return <PhoneInput value={value} onChange={setValue} defaultCountry={country} />;
    }
    const { rerender } = render(<Harness />);
    await user.type(screen.getByRole("textbox"), "69");

    rerender(<Harness country="PT" />);

    expect(screen.getByRole("combobox", { name: "Country code" })).toHaveTextContent("+355");
  });

  it("derives the nationally-formatted text and country from a full E.164 value", () => {
    render(<PhoneInput value="+351912345678" onChange={vi.fn()} />);
    expect(screen.getByRole("combobox", { name: "Country code" })).toHaveTextContent("+351");
    expect(screen.getByRole("textbox")).toHaveValue("912 345 678");
  });

  it("derives the country from a non-default E.164 value", () => {
    render(<PhoneInput value="+447911123456" onChange={vi.fn()} />);
    expect(screen.getByRole("combobox", { name: "Country code" })).toHaveTextContent("+44");
  });

  it("falls back to the default country and shows raw legacy text verbatim when unparseable", () => {
    render(<PhoneInput value="not-a-real-phone" onChange={vi.fn()} />);
    expect(screen.getByRole("combobox", { name: "Country code" })).toHaveTextContent("+355");
    expect(screen.getByRole("textbox")).toHaveValue("not-a-real-phone");
  });

  it("emits the full E.164 string once a valid Albanian national number is typed", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<ControlledPhoneInput onChange={onChange} />);

    await user.type(screen.getByRole("textbox"), "691234567");

    expect(onChange).toHaveBeenLastCalledWith("+355691234567");
  });

  it("emits a distinct, invalid value while the number is still incomplete", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<ControlledPhoneInput onChange={onChange} />);

    await user.type(screen.getByRole("textbox"), "912");

    const lastValue = onChange.mock.calls.at(-1)?.[0] as string;
    expect(lastValue).not.toBe("");
    expect(isValidPhoneNumber(lastValue)).toBe(false);
  });

  // Keeps its per-test budget. Typing the 10 digits is cheap since the calling-code items are
  // memoized (~0.2 s, measured 2026-11-07). What remains is opening the list, which mounts all
  // ~245 options (~0.8 s), finding one by accessible name across them (~0.9 s) and closing it, which
  // remounts them hidden (~0.7 s): ~2.5 s isolated, 10.7 s under the full suite with coverage on a
  // 2-core laptop. That is the cost of opening the list, which memoizing does not change.
  it("re-emits onChange with the new country's calling code applied to already-typed digits", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<ControlledPhoneInput onChange={onChange} />);

    await user.type(screen.getByRole("textbox"), "7911123456");
    onChange.mockClear();

    await user.click(screen.getByRole("combobox", { name: "Country code" }));
    await user.click(await screen.findByRole("option", { name: /United Kingdom/i }));

    const lastValue = onChange.mock.calls.at(-1)?.[0] as string;
    expect(lastValue.startsWith("+44")).toBe(true);
  }, 20000);

  it("puts aria-invalid and aria-describedby on the national input, not the select", () => {
    render(
      <PhoneInput
        value=""
        onChange={vi.fn()}
        aria-invalid
        aria-describedby="phone-error"
      />,
    );
    const textbox = screen.getByRole("textbox");
    expect(textbox).toHaveAttribute("aria-invalid", "true");
    expect(textbox).toHaveAttribute("aria-describedby", "phone-error");

    const combobox = screen.getByRole("combobox", { name: "Country code" });
    expect(combobox).not.toHaveAttribute("aria-invalid");
    expect(combobox).not.toHaveAttribute("aria-describedby");
  });
});
