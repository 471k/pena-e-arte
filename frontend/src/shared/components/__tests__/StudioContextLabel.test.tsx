import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import authReducer from "@/features/auth/authSlice";
import { StudioContextLabel } from "@/shared/components/StudioContextLabel";

const studioQuery = vi.fn();
vi.mock("@/features/studios/studiosApi", () => ({
  useGetMyStudioQuery: (...args: unknown[]) => studioQuery(...args),
}));

type Impersonation = { sessionId: string; studioId: string; studioName: string; expiresAt: string; reason: string | null };

function renderLabel(variant: "header" | "drawer", opts: { tenantId?: string | null; role?: string; impersonation?: Impersonation | null } = {}) {
  const store = configureStore({
    reducer: { auth: authReducer },
    preloadedState: {
      auth: {
        user: { id: "u1", email: "a@example.com", name: "A" },
        token: "t", refreshToken: null, pendingReferralCode: null,
        tenantId: opts.tenantId === undefined ? "studio-1" : opts.tenantId,
        role: opts.role ?? "owner",
        impersonation: opts.impersonation ?? null,
      // eslint-disable-next-line @typescript-eslint/no-explicit-any
      } as any,
    },
  });
  return render(<Provider store={store}><StudioContextLabel variant={variant} /></Provider>);
}

describe("StudioContextLabel (S08)", () => {
  beforeEach(() => studioQuery.mockReset());

  it("header: names the studio, labelled for a screen reader", () => {
    studioQuery.mockReturnValue({ data: { name: "Ink & Iron" }, isLoading: false });
    renderLabel("header");
    expect(screen.getByText("Ink & Iron")).toBeInTheDocument();
    expect(screen.getByText("Ink & Iron").parentElement).toHaveTextContent("Studio: Ink & Iron");
  });

  it("drawer: names the studio and the role", () => {
    studioQuery.mockReturnValue({ data: { name: "Ink & Iron" }, isLoading: false });
    renderLabel("drawer", { role: "artist" });
    expect(screen.getByText("Ink & Iron").parentElement).toHaveTextContent("Studio: Ink & Iron · Artist");
  });

  it("support session: uses the banner's studio name before the read lands, and never repeats the mode", () => {
    studioQuery.mockReturnValue({ data: undefined, isLoading: true });
    const { container } = renderLabel("header", {
      impersonation: { sessionId: "s", studioId: "studio-1", studioName: "Needle Point", expiresAt: "2099-01-01T00:00:00Z", reason: "x" },
    });
    expect(screen.getByText("Needle Point")).toBeInTheDocument();
    expect(container).not.toHaveTextContent(/viewing as|read-only/i);
  });

  it("loading without a name: a placeholder, not a guess", () => {
    studioQuery.mockReturnValue({ data: undefined, isLoading: true });
    const { container } = renderLabel("header");
    expect(container.querySelector(".animate-pulse")).not.toBeNull();
    expect(container.textContent).toBe("");
  });

  it("failed read: shows nothing", () => {
    studioQuery.mockReturnValue({ data: undefined, isLoading: false, isError: true });
    const { container } = renderLabel("header");
    expect(container).toBeEmptyDOMElement();
  });

  it("no active studio (a client who hasn't joined one): no label and no request", () => {
    studioQuery.mockReturnValue({ data: undefined, isLoading: false });
    const { container } = renderLabel("drawer", { tenantId: null, role: "client" });
    expect(container).toBeEmptyDOMElement();
    expect(studioQuery).toHaveBeenCalledWith(undefined, { skip: true });
  });
});
