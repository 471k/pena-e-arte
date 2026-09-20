import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { AtSign } from "lucide-react";
import { ConnectionRow, type ConnectionRowProps } from "@/features/social/components/ConnectionRow";

afterEach(cleanup);

type Overrides = Partial<ConnectionRowProps>;

function renderRow(overrides: Overrides = {}) {
  const props: ConnectionRowProps = {
    icon: AtSign,
    label: "TikTok",
    handle: null,
    isVerified: false,
    action: null,
    ...overrides,
  };
  return render(<ul><ConnectionRow {...props} /></ul>);
}

describe("ConnectionRow", () => {
  it("not linked, read-only: status text and no interactive control", () => {
    renderRow({ action: null });

    expect(screen.getByText("Not linked")).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
    expect(screen.queryByRole("textbox")).not.toBeInTheDocument();
  });

  it("not linked with OAuth: one Connect button, busy state disables it and says why", () => {
    const onClick = vi.fn();
    const { rerender } = renderRow({
      action: { kind: "connect", onClick },
      helper: "Connect to show a Verified badge.",
    });

    expect(screen.getByText("Connect to show a Verified badge.")).toBeInTheDocument();
    expect(screen.getAllByRole("button")).toHaveLength(1);
    expect(screen.getByRole("button", { name: "Connect TikTok" })).toBeEnabled();

    rerender(
      <ul>
        <ConnectionRow
          icon={AtSign} label="TikTok" handle={null} isVerified={false}
          action={{ kind: "connect", onClick, busy: true }}
        />
      </ul>,
    );
    expect(screen.getByRole("button", { name: "Connect TikTok" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Connect TikTok" })).toHaveAttribute("aria-busy", "true");
    expect(screen.getByText("Opening TikTok…")).toBeInTheDocument();
  });

  it("clicking the action fires its handler", async () => {
    const onClick = vi.fn();
    renderRow({ action: { kind: "connect", onClick } });

    await userEvent.setup().click(screen.getByRole("button", { name: "Connect TikTok" }));

    expect(onClick).toHaveBeenCalledTimes(1);
  });

  it("manual path: 'Get verification code' is disabled until there is a handle", () => {
    renderRow({ action: { kind: "get-code", onClick: vi.fn(), disabled: true } });
    expect(screen.getByRole("button", { name: "Get TikTok verification code" })).toBeDisabled();
  });

  it("handle added (unverified): shows 'Handle added' and the @handle", () => {
    renderRow({ handle: "inkbyrui", action: { kind: "get-code", onClick: vi.fn(), disabled: false } });

    expect(screen.getByText("Handle added")).toBeInTheDocument();
    expect(screen.getByText("@inkbyrui")).toBeInTheDocument();
  });

  it("verified: Verified badge, @handle and a Disconnect action", () => {
    const onClick = vi.fn();
    renderRow({ handle: "inkbyrui", isVerified: true, action: { kind: "disconnect", onClick } });

    expect(screen.getByText("Verified")).toBeInTheDocument();
    expect(screen.getByText("@inkbyrui")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Disconnect TikTok" })).toBeInTheDocument();
    expect(screen.queryByText("Not linked")).not.toBeInTheDocument();
  });

  it("unavailable: status and reason are visible text, no button", () => {
    renderRow({ action: { kind: "unavailable" } });

    expect(screen.getByText("Unavailable")).toBeInTheDocument();
    expect(screen.getByText("Not available on this server yet.")).toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("handle input: labelled, @ adornment decorative, mobile-keyboard-safe attributes, error linked via aria-describedby", async () => {
    const onChange = vi.fn();
    const onBlur = vi.fn();
    renderRow({
      action: { kind: "get-code", onClick: vi.fn(), disabled: false },
      handleInput: { value: "ink", onChange, onBlur, error: "Couldn't save the TikTok handle.", status: "idle" },
    });

    const input = screen.getByRole("textbox", { name: "TikTok handle" });
    expect(input).toHaveAttribute("autocapitalize", "off");
    expect(input).toHaveAttribute("autocorrect", "off");
    expect(input).toHaveAttribute("spellcheck", "false");
    expect(input).toHaveAttribute("aria-invalid", "true");
    expect(input).toHaveAccessibleDescription("Couldn't save the TikTok handle.");

    const user = userEvent.setup();
    await user.type(input, "x");
    expect(onChange).toHaveBeenCalled();
    await user.tab();
    expect(onBlur).toHaveBeenCalled();
  });

  it("handle input: announces Saving… and Saved through a polite live region", () => {
    const base = { value: "ink", onChange: vi.fn(), onBlur: vi.fn() };
    const { rerender } = renderRow({ handleInput: { ...base, status: "saving" } });
    expect(screen.getByRole("status")).toHaveTextContent("Saving…");

    rerender(
      <ul>
        <ConnectionRow
          icon={AtSign} label="TikTok" handle={null} isVerified={false} action={null}
          handleInput={{ ...base, status: "saved" }}
        />
      </ul>,
    );
    expect(screen.getByRole("status")).toHaveTextContent("Saved");
    expect(screen.getByRole("status")).toHaveAttribute("aria-live", "polite");
  });

  it("renders children inside the row's list item (e.g. Instagram's synced posts)", () => {
    renderRow({ children: <p>Synced posts panel</p> });
    expect(screen.getByRole("listitem")).toContainElement(screen.getByText("Synced posts panel"));
  });
});
