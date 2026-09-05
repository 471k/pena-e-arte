import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";

import { TattooIntakeFields } from "@/features/appointments/components/TattooIntakeFields";
import type { TattooIntakeValues } from "@/features/appointments/components/tattooIntakeValidation";

afterEach(() => cleanup());

const EMPTY: TattooIntakeValues = {
  tattooDescription: "", referralSource: "", referralSourceOther: "", safetyNotes: "",
};

function renderFields(value: TattooIntakeValues = EMPTY, onChange = vi.fn()) {
  render(<TattooIntakeFields value={value} onChange={onChange} />);
  return onChange;
}

describe("TattooIntakeFields", () => {
  it("renders the tattoo description field", () => {
    renderFields();
    expect(screen.getByLabelText(/what are you looking to get done/i)).toBeInTheDocument();
  });

  it("renders the referral source selector", () => {
    renderFields();
    expect(screen.getByLabelText(/how did you hear about us/i)).toBeInTheDocument();
  });

  it("renders the safety notes field", () => {
    renderFields();
    expect(screen.getByLabelText(/anything else i should know/i)).toBeInTheDocument();
  });

  it("does not show the 'tell us where' field until Other is selected", () => {
    renderFields();
    expect(screen.queryByLabelText(/tell us where you heard about us/i)).not.toBeInTheDocument();
  });

  it("shows the 'tell us where' field when referralSource is Other", () => {
    renderFields({ ...EMPTY, referralSource: "Other" });
    expect(screen.getByLabelText(/tell us where you heard about us/i)).toBeInTheDocument();
  });

  it("typing in the description field calls onChange with the updated value", async () => {
    const user = userEvent.setup();
    const onChange = renderFields();

    await user.type(screen.getByLabelText(/what are you looking to get done/i), "A");

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ tattooDescription: "A" }));
  });

  it("selecting a referral source option calls onChange with that option", async () => {
    const user = userEvent.setup();
    const onChange = renderFields();

    await user.click(screen.getByLabelText(/how did you hear about us/i));
    await user.click(await screen.findByRole("option", { name: "Instagram" }));

    expect(onChange).toHaveBeenCalledWith(expect.objectContaining({ referralSource: "Instagram" }));
  });

  it("shows the tattooDescriptionError message when provided", () => {
    render(
      <TattooIntakeFields
        value={EMPTY}
        onChange={vi.fn()}
        tattooDescriptionError="Tell us what you're looking to get done."
      />,
    );
    expect(screen.getByText("Tell us what you're looking to get done.")).toBeInTheDocument();
  });

  it("shows the referralSourceOtherError message when Other is selected and error is set", () => {
    render(
      <TattooIntakeFields
        value={{ ...EMPTY, referralSource: "Other" }}
        onChange={vi.fn()}
        referralSourceOtherError="Please tell us where."
      />,
    );
    expect(screen.getByText("Please tell us where.")).toBeInTheDocument();
  });
});
