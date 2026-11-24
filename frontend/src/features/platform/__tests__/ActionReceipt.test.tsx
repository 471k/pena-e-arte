import { describe, it, expect, vi, afterEach } from "vitest";
import { readFileSync } from "node:fs";
import { render, screen, cleanup } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { toast } from "sonner";
import { ActionReceiptArea, ActionReceiptNotice } from "../components/ActionReceipt";
import { AUDIT_ACTIONS, auditLogHref, makeReceipt, receiptFromState, useReportReceipt } from "../components/receipts";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn(), info: vi.fn() } }));

afterEach(() => { cleanup(); vi.clearAllMocks(); });

describe("ActionReceipt (D04)", () => {
  it("every action name is one the backend actually writes (AuditActions.cs)", () => {
    // A receipt's audit link filters by this string. A name that drifts from the command's own constant
    // would link to an empty audit log, which looks like "nothing was recorded".
    // The backend's own constants file, read from disk, so the comparison is against the real thing. The path is
    // relative to the frontend folder, where vitest runs (locally and in CI).
    const cs = readFileSync("../Pena_e_Arte.Domain/Constants/AuditActions.cs", "utf8");
    const backend = new Set([...cs.matchAll(/public const string \w+ = "([^"]+)";/g)].map((m) => m[1]));
    for (const [key, value] of Object.entries(AUDIT_ACTIONS)) {
      expect(backend.has(value), `${key} = "${value}" is not in AuditActions.cs`).toBe(true);
    }
  });

  it("links to the audit log filtered to the action, and to the studio when there is one", () => {
    expect(auditLogHref("Studio.Suspended", "s-1")).toBe("/platform/audit-log?studioId=s-1&action=Studio.Suspended");
    expect(auditLogHref("Plan.Deleted")).toBe("/platform/audit-log?action=Plan.Deleted");
  });

  it("a receipt names what happened, when, links the audit log, and has a named dismiss", async () => {
    const onDismiss = vi.fn();
    render(
      <MemoryRouter>
        <ActionReceiptNotice receipt={makeReceipt("Ink Soul is suspended.", AUDIT_ACTIONS.studioSuspended, "s-1")} onDismiss={onDismiss} />
      </MemoryRouter>,
    );
    const status = screen.getByRole("status");
    expect(status).toHaveTextContent("Ink Soul is suspended.");
    expect(status).toHaveTextContent("UTC");
    expect(screen.getByRole("link", { name: /view in the audit log/i }))
      .toHaveAttribute("href", "/platform/audit-log?studioId=s-1&action=Studio.Suspended");
    await userEvent.setup().click(screen.getByRole("button", { name: "Dismiss this receipt" }));
    expect(onDismiss).toHaveBeenCalledOnce();
  });

  it("reads a receipt from router state only when it has the right shape", () => {
    const good = makeReceipt("Done.", AUDIT_ACTIONS.planCreated);
    expect(receiptFromState({ receipt: good })).toEqual({ ...good, studioId: null });
    expect(receiptFromState(null)).toBeNull();
    expect(receiptFromState({ receipt: { summary: 1 } })).toBeNull();
    expect(receiptFromState({ other: good })).toBeNull();
  });

  it("a row's receipt shows in the page's area and outlives the row", async () => {
    function Row() {
      const report = useReportReceipt();
      return <button type="button" onClick={() => report(makeReceipt("Code ABC is deleted.", AUDIT_ACTIONS.referralCodeDeleted))}>Delete</button>;
    }
    function Page({ showRow }: { showRow: boolean }) {
      return <MemoryRouter><ActionReceiptArea>{showRow && <Row />}</ActionReceiptArea></MemoryRouter>;
    }
    const { rerender } = render(<Page showRow />);
    await userEvent.setup().click(screen.getByRole("button", { name: "Delete" }));
    rerender(<Page showRow={false} />);
    expect(screen.getByRole("status")).toHaveTextContent("Code ABC is deleted.");
  });

  it("outside an area, a receipt falls back to a toast rather than vanishing", async () => {
    function Lone() {
      const report = useReportReceipt();
      return <button type="button" onClick={() => report(makeReceipt("Done.", AUDIT_ACTIONS.planDeleted))}>Go</button>;
    }
    render(<Lone />);
    await userEvent.setup().click(screen.getByRole("button", { name: "Go" }));
    expect(toast.success).toHaveBeenCalledWith("Done.");
  });
});
