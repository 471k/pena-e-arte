import { describe, it, expect, beforeAll, afterEach, afterAll } from "vitest";
import { render, screen, cleanup } from "@testing-library/react";
import { Provider } from "react-redux";
import { configureStore } from "@reduxjs/toolkit";
import { http, HttpResponse } from "msw";
import { setupServer } from "msw/node";

import { publicApi, type ExchangeRateResponse } from "@/features/public/publicApi";
import { Money } from "@/shared/components/Money";
import { MoneyStudioContext } from "@/shared/components/moneyStudioContext";

const RATE: ExchangeRateResponse = { rate: 90.74, rateDate: "2026-10-23", source: "BoA-Fix", isStale: false };

let rateRequests = 0;
const server = setupServer(
  http.get("http://localhost/api/v1/public/exchange-rates/eur-all", () => {
    rateRequests++;
    return HttpResponse.json(RATE);
  }),
);

beforeAll(() => server.listen({ onUnhandledRequest: "error" }));
afterEach(() => { server.resetHandlers(); cleanup(); rateRequests = 0; });
afterAll(() => server.close());

function renderMoney(ui: React.ReactElement) {
  const store = configureStore({
    reducer: { [publicApi.reducerPath]: publicApi.reducer },
    middleware: (gd) => gd().concat(publicApi.middleware),
  });
  render(<Provider store={store}>{ui}</Provider>);
}

// Intl puts a no-break space between "ALL" and the number; \s matches it.
const AL_EUR = { countryCode: "AL", currency: "EUR" };

describe("<Money>", () => {
  it("shows the approximate lek at the latest rate for an Albanian EUR studio", async () => {
    renderMoney(<Money amount={50} currency="EUR" studio={AL_EUR} />);
    expect(await screen.findByText(/^€50 \(≈ ALL\s4,537\)$/)).toBeInTheDocument();
  });

  it("shows the euro amount alone while the rate loads", () => {
    renderMoney(<Money amount={50} currency="EUR" studio={AL_EUR} />);
    expect(screen.getByText("€50")).toBeInTheDocument();
  });

  it("shows the euro amount alone, with no error, when no rate exists yet", async () => {
    server.use(http.get("http://localhost/api/v1/public/exchange-rates/eur-all", () => {
      rateRequests++;
      return HttpResponse.json({ message: "Not found" }, { status: 404 });
    }));
    renderMoney(<Money amount={50} currency="EUR" studio={AL_EUR} />);
    await waitForRequests(1);
    expect(screen.getByText("€50")).toBeInTheDocument();
    expect(screen.queryByText(/error|unavailable/i)).not.toBeInTheDocument();
  });

  it("shows the frozen lek without ≈ and fetches no rate", () => {
    renderMoney(
      <Money amount={50} currency="EUR" studio={AL_EUR} frozen={{ referenceAmount: 4500, referenceCurrency: "ALL" }} />,
    );
    expect(screen.getByText(/^€50 \(ALL\s4,500\)$/)).toBeInTheDocument();
    expect(rateRequests).toBe(0);
  });

  it("names the day of a stale rate", async () => {
    server.use(http.get("http://localhost/api/v1/public/exchange-rates/eur-all", () =>
      HttpResponse.json({ ...RATE, isStale: true })));
    renderMoney(<Money amount={50} currency="EUR" studio={AL_EUR} />);
    expect(await screen.findByText(/^€50 \(≈ ALL\s4,537 at the 23 Oct rate\)$/)).toBeInTheDocument();
  });

  it("is unchanged and makes no rate request for a studio in Albania that prices in lek", async () => {
    renderMoney(<Money amount={5000} currency="ALL" studio={{ countryCode: "AL", currency: "ALL" }} />);
    expect(screen.getByText(/^ALL\s5,000$/)).toBeInTheDocument();
    await new Promise((r) => setTimeout(r, 50));
    expect(rateRequests).toBe(0);
  });

  it("is unchanged and makes no rate request for a euro studio outside Albania", async () => {
    renderMoney(<Money amount={50} currency="EUR" studio={{ countryCode: "XK", currency: "EUR" }} />);
    expect(screen.getByText("€50")).toBeInTheDocument();
    await new Promise((r) => setTimeout(r, 50));
    expect(rateRequests).toBe(0);
  });

  it("on staff screens takes the studio from MoneyStudioContext (Q3)", async () => {
    renderMoney(
      <MoneyStudioContext.Provider value={AL_EUR}>
        <Money amount={50} currency="EUR" />
      </MoneyStudioContext.Provider>,
    );
    expect(await screen.findByText(/^€50 \(≈ ALL\s4,537\)$/)).toBeInTheDocument();
  });

  it("without a studio prop or context shows the plain amount and fetches nothing", async () => {
    renderMoney(<Money amount={50} currency="EUR" />);
    expect(screen.getByText("€50")).toBeInTheDocument();
    await new Promise((r) => setTimeout(r, 50));
    expect(rateRequests).toBe(0);
  });

  it("converts any amount on a paid record at its frozen rate (refunds, balances)", () => {
    renderMoney(
      <Money amount={30} currency="EUR" studio={AL_EUR}
        frozen={{ referenceAmount: 9074, referenceCurrency: "ALL", referenceRate: 90.74 }} />,
    );
    expect(screen.getByText(/^€30 \(ALL\s2,722\)$/)).toBeInTheDocument();
    expect(rateRequests).toBe(0);
  });

  it("keeps the euro and lek amounts in one element, so they read together", async () => {
    renderMoney(<Money amount={50} currency="EUR" studio={AL_EUR} className="font-semibold" />);
    const el = await screen.findByText(/^€50 \(≈ ALL\s4,537\)$/);
    expect(el.tagName).toBe("SPAN");
    expect(el).toHaveClass("font-semibold");
  });
});

async function waitForRequests(n: number) {
  const until = Date.now() + 3000;
  while (rateRequests < n && Date.now() < until) await new Promise((r) => setTimeout(r, 20));
  // Let RTK Query settle the error state after the response.
  await new Promise((r) => setTimeout(r, 20));
}
