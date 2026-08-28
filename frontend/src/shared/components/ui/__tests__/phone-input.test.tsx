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
  it("renders with an empty value showing the default country and an empty national input", () => {
    render(<PhoneInput value="" onChange={vi.fn()} />);
    expect(screen.getByRole("combobox", { name: "Country code" })).toHaveTextContent("+351");
    expect(screen.getByRole("textbox")).toHaveValue("");
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
    expect(screen.getByRole("combobox", { name: "Country code" })).toHaveTextContent("+351");
    expect(screen.getByRole("textbox")).toHaveValue("not-a-real-phone");
  });

  it("emits the full E.164 string once a valid PT national number is typed", async () => {
    const user = userEvent.setup();
    const onChange = vi.fn();
    render(<ControlledPhoneInput onChange={onChange} />);

    await user.type(screen.getByRole("textbox"), "912345678");

    expect(onChange).toHaveBeenLastCalledWith("+351912345678");
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
  });

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
