import { describe, it, expect, vi, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup, within, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes, useNavigate } from "react-router-dom";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import authReducer from "@/features/auth/authSlice";
import { studiosApi, type SetupReadinessResponse } from "@/features/studios/studiosApi";
import { StudioSettingsSections } from "@/features/studios/components/StudioSettingsSections";
import { useSettingsGroups } from "@/features/studios/useSettingsGroups";
import { SETTINGS_GROUPS, isSettingsGroupId } from "@/features/studios/settingsGroups";
import { SetupChecklist } from "@/features/dashboard/components/SetupChecklist";
import { OnboardingTour } from "@/shared/components/OnboardingTour";
import { ownerTourSteps } from "@/features/help/tours/ownerTour";
import { HELP_ARTICLES } from "@/features/help/helpContent";

// E05c (O04): Studio Settings grouped by job. Every card is a stub carrying the real card's tour target,
// so this file mounts the grouping alone (StudioProfilePage.test.tsx mounts the whole page, and nothing
// is added to it).

vi.mock("@/features/studios/components/CurrencySettingsCard", async () => ({ CurrencySettingsCard: (await import("./settingsCardStub")).makeCardStub("Currency", "owner-studio-currency-card") }));
vi.mock("@/features/studios/components/PokSettingsCard", async () => ({ PokSettingsCard: (await import("./settingsCardStub")).makeCardStub("Pok", "owner-pok-card") }));
vi.mock("@/features/studios/components/StudioPayModelCard", async () => ({ StudioPayModelCard: (await import("./settingsCardStub")).makeCardStub("PayModel", "owner-studio-pay-model-card") }));
vi.mock("@/features/studios/components/StudioHoursCard", async () => ({ StudioHoursCard: (await import("./settingsCardStub")).makeCardStub("Hours", "owner-studio-hours-card") }));
vi.mock("@/features/studios/components/StudioClosuresCard", async () => ({ StudioClosuresCard: (await import("./settingsCardStub")).makeCardStub("Closures") }));
vi.mock("@/features/studios/components/StudioSocialLinksCard", async () => ({ StudioSocialLinksCard: (await import("./settingsCardStub")).makeCardStub("Social") }));
vi.mock("@/features/studios/components/BrandingSettingsCard", async () => ({ BrandingSettingsCard: (await import("./settingsCardStub")).makeCardStub("Branding") }));
vi.mock("@/features/studios/components/EmbedCodeCard", async () => ({ EmbedCodeCard: (await import("./settingsCardStub")).makeCardStub("Embed") }));
vi.mock("@/features/studios/components/QrCodeSection", async () => ({ QrCodeSection: (await import("./settingsCardStub")).makeCardStub("Qr") }));
vi.mock("@/features/studios/components/DeveloperSettingsCard", async () => ({ DeveloperSettingsCard: (await import("./settingsCardStub")).makeCardStub("Developer") }));
vi.mock("@/features/studios/components/WebhookSettingsCard", async () => ({ WebhookSettingsCard: (await import("./settingsCardStub")).makeCardStub("Webhooks") }));
vi.mock("@/features/studios/components/ReferralCodeCard", async () => ({ ReferralCodeCard: (await import("./settingsCardStub")).makeCardStub("Referral") }));
vi.mock("@/features/studios/components/StudioAuditLogCard", async () => ({ StudioAuditLogCard: (await import("./settingsCardStub")).makeCardStub("AuditLog") }));
vi.mock("@/features/notifications/components/NotificationPreferencesCard", async () => ({ NotificationPreferencesCard: (await import("./settingsCardStub")).makeCardStub("NotificationPrefs") }));

const EXPECTED: Record<string, string[]> = {
  profile:       ["Details", "Social"],
  hours:         ["Hours", "Closures"],
  money:         ["Currency", "Pok", "PayModel"],
  booking:       ["Embed", "Branding", "Qr"],
  notifications: ["NotificationPrefs"],
  integrations:  ["Developer", "Webhooks"],
  referrals:     ["Referral"],
  activity:      ["AuditLog"],
};

/** The page's own part (StudioProfilePage's URL card and details form), as StudioProfilePage passes it. */
const PROFILE = <div data-testid="card-Details"><button type="button">Details action</button></div>;

function SettingsHarness() {
  const groups = useSettingsGroups();
  return <StudioSettingsSections profile={PROFILE} groups={groups} />;
}

/** Lets a test navigate while the page stays mounted, the way the tour and the header links do. */
function NavigateButton({ to }: { to: string }) {
  const navigate = useNavigate();
  return <button type="button" onClick={() => navigate(to)}>go {to}</button>;
}

const server = setupServer(
  http.get("http://localhost/api/v1/studios/me/setup-readiness", () =>
    HttpResponse.json({
      areas: [
        { area: "Artists", state: "Reviewed", reviewedAt: null },
        { area: "DepositRules", state: "Reviewed", reviewedAt: null },
        { area: "Hours", state: "DefaultUnreviewed", reviewedAt: null },
        { area: "PayModel", state: "DefaultUnreviewed", reviewedAt: null },
      ],
    } satisfies SetupReadinessResponse)),
);
beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); });
afterAll(() => server.close());

function renderAt(path: string, extra?: React.ReactNode) {
  const store = configureStore({
    reducer: { auth: authReducer, [studiosApi.reducerPath]: studiosApi.reducer },
    middleware: (gd) => gd().concat(studiosApi.middleware),
  });
  render(
    <Provider store={store}>
      <MemoryRouter initialEntries={[path]}>
        <Routes>
          <Route path="/dashboard" element={<SetupChecklist />} />
          <Route path="/studios/me" element={<><SettingsHarness />{extra}</>} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

const group = (id: string) => screen.getByTestId(`settings-group-${id}`);
const toggle = (title: string) => screen.getByRole("button", { name: title });
/** A card is shown when nothing between it and its group is `hidden`. */
const isShown = (testId: string) => screen.getByTestId(testId).closest("[hidden]") === null;

describe("Studio Settings groups (E05c, O04)", () => {
  it("puts every card in its group, each card exactly once", () => {
    renderAt("/studios/me");
    for (const [id, cards] of Object.entries(EXPECTED)) {
      const inGroup = within(group(id)).getAllByTestId(/^card-/).map((el) => el.dataset.testid);
      expect(inGroup, id).toEqual(cards.map((c) => `card-${c}`));
    }
    expect(screen.getAllByTestId(/^card-/)).toHaveLength(Object.values(EXPECTED).flat().length);
    expect(SETTINGS_GROUPS.map((g) => g.id)).toEqual(Object.keys(EXPECTED));
  });

  it("shows Business details as a plain heading that can't collapse, and starts every other group closed", () => {
    renderAt("/studios/me");
    expect(screen.getByRole("heading", { level: 2, name: "Business details" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Business details" })).not.toBeInTheDocument();
    expect(isShown("card-Details")).toBe(true);
    for (const g of SETTINGS_GROUPS.filter((x) => x.id !== "profile")) {
      expect(toggle(g.title), g.id).toHaveAttribute("aria-expanded", "false");
    }
    expect(isShown("card-Hours")).toBe(false);
  });

  // The studio's map is in Business details. Leaflet mounted inside a hidden (0x0) group threw
  // "Invalid LatLng object: (NaN, NaN)" and took the page down when a link opened another group
  // (390 px browser check). Whatever a link opens, Business details stays laid out.
  it.each(["hours", "money", "booking", "activity"])("keeps Business details shown when a link opens %s", (section) => {
    renderAt(`/studios/me?section=${section}`);
    expect(isShown("card-Details")).toBe(true);
  });

  it("keeps a collapsed group's contents out of the tab order", async () => {
    const user = userEvent.setup();
    renderAt("/studios/me");
    const reached: string[] = [];
    for (let i = 0; i < 20; i++) {
      await user.tab();
      reached.push(document.activeElement?.textContent ?? "");
    }
    expect(reached).toContain("Details action");
    expect(reached.some((t) => t.startsWith("Hours and closures"))).toBe(true);
    expect(reached.some((t) => /^(Hours|Currency|Pok|PayModel|Webhooks) action$/.test(t))).toBe(false);
  });

  it("opens and closes from the keyboard, and the contents become reachable", async () => {
    const user = userEvent.setup();
    renderAt("/studios/me");
    toggle("Hours and closures").focus();

    await user.keyboard("{Enter}");
    expect(toggle("Hours and closures")).toHaveAttribute("aria-expanded", "true");
    expect(isShown("card-Hours")).toBe(true);
    await user.tab();
    expect(document.activeElement).toHaveTextContent("Hours action");

    toggle("Hours and closures").focus();
    await user.keyboard(" ");
    expect(toggle("Hours and closures")).toHaveAttribute("aria-expanded", "false");
    expect(isShown("card-Hours")).toBe(false);
  });

  it("names its contents under each heading, without changing the button's name", () => {
    renderAt("/studios/me");
    expect(toggle("Payments and artist pay")).toHaveAccessibleDescription(/currency, card deposits with pok/i);
  });

  it.each([
    ["hours", "card-Hours"],
    ["money", "card-PayModel"],
    ["integrations", "card-Webhooks"],
  ])("a link to ?section=%s opens that group", (section, card) => {
    renderAt(`/studios/me?section=${section}`);
    expect(isShown(card)).toBe(true);
  });

  it("ignores a section it doesn't know: nothing extra opens", () => {
    renderAt("/studios/me?section=nonsense");
    expect(isShown("card-Details")).toBe(true);
    expect(screen.queryAllByRole("button", { expanded: true })).toHaveLength(0);
  });

  it("opens the named group on every navigation, even to the URL already showing", async () => {
    const user = userEvent.setup();
    renderAt("/studios/me", <NavigateButton to="/studios/me?section=money" />);
    expect(isShown("card-Currency")).toBe(false);

    await user.click(screen.getByRole("button", { name: "go /studios/me?section=money" }));
    expect(isShown("card-Currency")).toBe(true);

    await user.click(toggle("Payments and artist pay"));
    expect(isShown("card-Currency")).toBe(false);
    await user.click(screen.getByRole("button", { name: "go /studios/me?section=money" }));
    expect(isShown("card-Currency")).toBe(true);
  });

  it.each([
    ["Check hours", "card-Hours"],
    ["Set pay model", "card-PayModel"],
  ])("the setup checklist's %s lands with its card's group open", async (cta, card) => {
    const user = userEvent.setup();
    renderAt("/dashboard");
    await user.click(await screen.findByRole("button", { name: cta }));
    expect(isShown(card)).toBe(true);
  });

  it("each card-level tour step on Studio Settings finds its card open when the step shows", async () => {
    const steps = ownerTourSteps.filter((s) => s.route?.startsWith("/studios/me"));
    expect(steps.map((s) => s.targetSelector)).toEqual([
      '[data-tour="owner-studio-hours-card"]',
      '[data-tour="owner-studio-currency-card"]',
      '[data-tour="owner-studio-pay-model-card"]',
      '[data-tour="owner-pok-card"]',
    ]);
    const user = userEvent.setup();
    const onComplete = vi.fn();
    // Start on the page with only Business details open: the steps have to open the groups themselves.
    renderAt("/studios/me", <OnboardingTour steps={steps} onComplete={onComplete} onSkip={vi.fn()} />);

    for (const [i, step] of steps.entries()) {
      await screen.findByRole("dialog", { name: step.title });
      const target = document.querySelector(step.targetSelector);
      expect(target, step.title).not.toBeNull();
      expect(target!.closest("[hidden]"), `${step.title} spotlit inside a closed group`).toBeNull();
      await user.click(screen.getByRole("button", { name: i === steps.length - 1 ? /finish|done/i : /next/i }));
    }
    await waitFor(() => expect(onComplete).toHaveBeenCalled());
  });

  it("every Help 'Go to page' link into Studio Settings names a group that exists", () => {
    const routes = HELP_ARTICLES.map((a) => a.route).filter((r): r is string => !!r && r.startsWith("/studios/me?"));
    expect(routes.length).toBeGreaterThan(0);
    for (const r of routes) {
      expect(isSettingsGroupId(new URL(r, "http://x").searchParams.get("section")), r).toBe(true);
    }
  });
});
