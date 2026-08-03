# POK Payments Documentation

> Integration documentation for POK Payments — JavaScript / React library, PHP SDK, REST API, and e-commerce plugins.

This is the LLM-readable index of the POK Payments Documentation. Each link points to a markdown source file you can fetch directly to integrate POK Payments into an application.

## JavaScript

- [POK Payments JS](https://docs.pokpay.io/docs/pok-js.md): The official JavaScript SDK for accepting card payments with POK — React, vanilla JS, CDN, and React Native.
- [React](https://docs.pokpay.io/docs/react.md): React integration guide — guest checkout, save card, pay with saved card, and custom UI with @nebula-ltd/pok-payments-js.
- [Vanilla JS (npm)](https://docs.pokpay.io/docs/vanilla-js.md): Use @nebula-ltd/pok-payments-js without React — encryptCard() for guest checkout, card tokenization, and saved-card flows with your own UI.
- [CDN](https://docs.pokpay.io/docs/cdn.md): Add POK Payments to any HTML page with a single script tag — no npm, no build step, no framework required.

## Mobile

- [React Native](https://docs.pokpay.io/docs/react-native.md): Drop-in components and primitives for accepting card payments in React Native apps — from @nebula-ltd/pok-payments-rn.
- [Flutter](https://docs.pokpay.io/docs/flutter.md): Flutter SDK for accepting card payments with native JWE encryption and natively presented 3DS challenges — pok_payments_flutter (Beta).

## Libraries

- [PHP SDK](https://docs.pokpay.io/docs/php-sdk.md): Server-side PHP SDK for creating, capturing, and confirming POK Payments orders. Wraps the Checkout REST API with typed model classes.

## Plugins

- [WooCommerce plugin](https://docs.pokpay.io/docs/woocommerce.md): Accept POK Payments in any WooCommerce store. Drop-in installer, configuration walkthrough, and go-live checklist.
- [PrestaShop plugin](https://docs.pokpay.io/docs/prestashop.md): Accept POK Payments in PrestaShop. Plugins for both PrestaShop 1.7 and 1.6, with installation, configuration, and testing steps.

## Reference

- [REST API](https://docs.pokpay.io/docs/rest-api.md): REST integration guide — staging quick start, authentication, SDK orders, and links to the full API reference at payments.doc.pokpay.io.

## Optional

- [Full documentation bundle](https://docs.pokpay.io/llms-full.txt): All documentation pages concatenated as a single markdown file.
=========================================================================
# POK Payments Documentation

> Integration documentation for POK Payments — JavaScript / React library, PHP SDK, REST API, and e-commerce plugins.

Concatenated documentation for AI assistants and code-generation tools.

---

# POK Payments JS

> The official JavaScript SDK for accepting card payments with POK — React, vanilla JS, CDN, and React Native.

# POK Payments JS

`@nebula-ltd/pok-payments-js` is the official JavaScript SDK for accepting card payments with POK. It ships multiple integration paths from the same package — pick the one that fits your stack.

| Path                            | Best for                                                                           |
| ------------------------------- | ---------------------------------------------------------------------------------- |
| [React components](/react)      | React 17+ apps. Drop in `<GuestCheckoutForm />` or `<AddCardForm />`.              |
| [React Native](/react-native)   | iOS and Android apps. Drop-in components with native JWE encryption and 3DS.       |
| [Vanilla JS / npm](/vanilla-js) | Any JS environment without React — bundled apps, server-rendered pages, custom UI. |
| [CDN](/cdn)                     | No build step. Drop a script tag into any HTML page.                               |
| [PHP SDK](/php-sdk)             | Server-side order creation, capture, and confirmation.                             |

All paths share the same callback signatures, configuration options, and security model.

> [!note]
> **Full TypeScript support.** Types are bundled with the npm package — autocomplete and inline documentation work out of the box in any TypeScript or modern JavaScript editor.

---

## Installation

### npm

```bash
npm install @nebula-ltd/pok-payments-js
```

If you are on React 17 or older, add `--legacy-peer-deps`:

```bash
npm install @nebula-ltd/pok-payments-js --legacy-peer-deps
```

### CDN (no build step)

```html
<script src="https://static.pokpay.io/public/dist/pokpayments/pok-payment.js"></script>
```

Exposes a global `PokPayment` object as soon as the script loads.

---

## Core concepts

### The four primitives

Every payment flow is built from one or more of these:

| Primitive               | Purpose                                                                                     |
| ----------------------- | --------------------------------------------------------------------------------------------|
| **Guest checkout form** | Complete form that collects card details, runs 3-D Secure, and captures a one-off payment.  |
| **Add-card form**       | Tokenizes a card without charging it. Your backend stores the returned `cardId` for later.  |
| **Pay-by-card-token**   | Charges a previously saved `cardId`. Requires a new SDK order and backend 3-DS setup first. |
| **`encryptCard()`**     | Low-level function that encrypts raw card data into a short-lived JWE token for custom UIs. |

**Typical vault flow:** save a card first (add-card form or `encryptCard` + Tokenize API), then pay with saved card on a later checkout. See [React](/react), [CDN](/cdn), [React Native](/react-native), or [Vanilla JS](/vanilla-js) for step-by-step guides.

### Environments

Controlled via the `env` option on every entry point:

- `production` — live charges against real cards. **Default.**
- `staging` — test environment. Use the [test cards](#test-cards); no money moves.

### Locales

Form labels and error messages support: `en` (English, default), `it` (Italian), `al` (Albanian).

### Country field UI (React / CDN)

Pass **`countrySelect`** inside the form **`options`** object on [React](/react) and [CDN](/cdn) integrations:

| Value        | Behavior                           |
| ------------ | ----------------------------------- |
| `'dropdown'` | Inline country list (default)      |
| `'modal'`    | Modal / full-screen country picker |

---

## Customizing styles

All styles are scoped under `#pok-payment-container`. Every override must include that prefix.

```css
#pok-payment-container .pok-payment-button {
  background-color: #0062a5;
  border-radius: 8px;
}

#pok-payment-container .pok-payment-input {
  border-color: #333;
  border-radius: 6px;
}

#pok-payment-container .pok-payment-error-message {
  background-color: #2a0000;
  border-color: #ff4d4d;
}
```

Override any built-in rule by targeting its selector under `#pok-payment-container` (for example `.pok-payment-button`, `.pok-payment-input`, `.pok-payment-modal-backdrop`).

For the full list of classes, layout rules, and country UI hooks, see the SDK stylesheet source on GitHub: https://github.com/pokpay-ltd/pok-payments-js/blob/develop/src/index.css.

The same file is published in the npm package as `@nebula-ltd/pok-payments-js/lib/index.css` (import it once at your app root on [React](/react)).

---

## Handling errors

All error callbacks receive a `PaymentErrorResponse` with at minimum a `message` field. Do not show the raw message to end users — log it server-side and show a friendly retry prompt.

- **Invalid / expired card** — show "Please check your card details" and let the user retry.
- **3-D Secure rejection** — the issuer declined the challenge. The user must contact their bank or use another card.
- **Order already paid / not found** — your backend created a stale or duplicate order. Recreate it before retrying.
- **Network failure** — retry safely; the SDK is idempotent against the same order ID.

---

## Test cards

Use these in **`staging`** only — no real money moves. Any future MM/YY expiry and any 3-digit CVV work.

```test-cards
4242 4242 4242 4242 | Visa — no 3DS
4000 0000 0000 1091 | Visa — 3DS challenge
4000 0000 0000 1026 | Visa — frictionless 3DS
5200 0000 0000 1005 | Mastercard — 3DS challenge
```

---

## Going to production

1. Remove any test card numbers from `initialState` — never ship hardcoded cards.
2. Ensure your backend has production POK API credentials and is hitting `api.pokpay.io`.
3. Smoke-test with a real low-value transaction.
4. Confirm your error logging captures `PaymentErrorResponse` payloads server-side.

---

# React

> React integration guide — guest checkout, save card, pay with saved card, and custom UI with @nebula-ltd/pok-payments-js.

# React

Install the package and import the stylesheet once at your app root:

```bash
npm install @nebula-ltd/pok-payments-js
```

```jsx
import "@nebula-ltd/pok-payments-js/lib/index.css";
```

On React 17 or older, add `--legacy-peer-deps`. TypeScript types are bundled — no `@types/*` package needed.

This package **supports React 19**. Use it with `GuestCheckoutForm`, `AddCardForm`, and `usePOK` the same way as on React 18.

If `npm install` warns about peer dependencies (the published range may still list React 18), install with:

```bash
npm install @nebula-ltd/pok-payments-js --legacy-peer-deps
```

---

## Base URLs (staging vs production)

Your **backend** calls the REST API on one of these hosts. The React SDK uses **`options.env`** / hook **`environment`** — it must match the host and credentials you use on the server.

| Environment               | API base URL                    | React `env` / `environment` | Credentials                                             |
| -------------------------- | -------------------------------- | ----------------------------- | --------------------------------------------------------- |
| **Staging** (development) | `https://api-staging.pokpay.io` | `'staging'`                 | Staging `keyId` / `keySecret` from the PokPay dashboard |
| **Production** (live)     | `https://api.pokpay.io`         | `'production'` (default)    | Production `keyId` / `keySecret`                        |

> [!warning]
> **Do not mix environments.** Staging keys on `api.pokpay.io`, or production keys on `api-staging.pokpay.io`, will fail (often `401`). Use [test cards](/pok-js#test-cards) only on **staging**.

---

## Choose your integration

| Goal                | React API                   | Your backend must                                                                                                           |
| -------------------- | ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------ |
| Pay once (new card) | `GuestCheckoutForm`         | Create SDK order → pass `orderId` ([REST API](/rest-api), [PHP SDK](/php-sdk))                                              |
| Save card for later | `AddCardForm`               | Accept payload → [Tokenize Card API](https://payments.doc.pokpay.io/#741b843c-a3e9-4dc6-afad-e575e7ecc7b4) → store `cardId` |
| Pay with saved card | `usePOK` + `payByCardToken` | New SDK order + `setup-tokenized-3ds` → return `payerAuthentication`                                                        |
| Custom card inputs  | `encryptCard()`             | Same tokenize exchange as save card                                                                                         |

> [!note]
> **Server vs browser:** `keyId` / `keySecret` and `POST .../sdk-orders` run **only on your server**. React receives **`orderId`**, **`AddCardData`**, or **`payerAuthentication`** — never merchant secrets. Use **`env: 'staging'`** with `api-staging.pokpay.io` and [test cards](/pok-js#test-cards) while developing.

---

## Guest checkout

`GuestCheckoutForm` collects card details, runs 3-D Secure, and captures the payment in a single flow. This is the primary path for one-time payments.

> [!important]
> **Create the order on your backend first.** Call `POST /merchants/{merchantId}/sdk-orders` server-side and pass the returned `orderId` as a prop. See the [REST API](/rest-api) on-ramp, [PHP SDK](/php-sdk), or the full HTTP reference at [payments.doc.pokpay.io](https://payments.doc.pokpay.io/). Never create orders from the browser — your API credentials must stay on the server.

```jsx
import { GuestCheckoutForm } from "@nebula-ltd/pok-payments-js/react";
import { PaymentErrorResponse } from "@nebula-ltd/pok-payments-js";

function CheckoutPage({ orderId }: { orderId: string }) {
  return (
    <GuestCheckoutForm
      orderId={orderId}
      onSuccess={() => {
        window.location.href = "/order-confirmed";
      }}
      onError={(error: PaymentErrorResponse) => {
        console.error(error);
      }}
      options={{ env: "staging", locale: "en", countrySelect: "modal" }}
    />
  );
}
```

### Props

| Prop        | Type                                    | Required | Description                                          |
| ------------ | ----------------------------------------- | ---------- | ------------------------------------------------------ |
| `orderId`   | `string`                                | Yes      | SDK order ID from your backend.                      |
| `onSuccess` | `() => void`                            | No       | Fires after the payment is captured.                 |
| `onError`   | `(error: PaymentErrorResponse) => void` | No       | Fires on any tokenization, 3-DS, or capture failure. |
| `options`   | `object`                                | No       | `env`, `locale`, `countrySelect`, `initialState`.    |

### options

Shared by `GuestCheckoutForm` and `AddCardForm`.

| Field           | Type                        | Default        | Description                                             |
| ---------------- | ----------------------------- | ---------------- | ----------------------------------------------------------|
| `env`           | `'production' \| 'staging'` | `'production'` | Use `'staging'` with test cards.                        |
| `locale`        | `'en' \| 'it' \| 'al'`      | `'en'`         | Form language.                                          |
| `countrySelect` | `'dropdown' \| 'modal'`     | `'dropdown'`   | How the billing **country** field is shown — see below. |
| `initialState`  | `object`                    | —              | Pre-fills form fields.                                  |

**`countrySelect` values:**

| Value        | UX                                                                |
| ------------ | -------------------------------------------------------------------|
| `'dropdown'` | Inline country selector in the form (default).                    |
| `'modal'`    | Opens a modal list to pick the country — better on small screens. |

### Advanced: initialState fields

Optional pre-fill for demos or power users — skip on first integration.

| Field                | Type     | Description                                          |
| --------------------- | ---------- | ------------------------------------------------------|
| `cardNumber`         | `string` | Visa, Visa Electron, Mastercard, Maestro.            |
| `email`              | `string` | Customer email.                                      |
| `expiration`         | `string` | `MM/YY`, future date.                                |
| `securityCode`       | `string` | CVV/CVC, max 3 characters.                           |
| `holdersName`        | `string` | Name as it appears on the card.                      |
| `countryCode`        | `string` | ISO 3166-1 alpha-2. Determines address fields shown. |
| `address1`           | `string` | Street address line 1.                               |
| `locality`           | `string` | City (auto-shown for US/CA).                         |
| `administrativeArea` | `string` | State or province code.                              |
| `postalCode`         | `string` | Postal or ZIP code.                                  |
| `phoneNumber`        | `string` | E.164 format.                                        |

---

## Save a card

`AddCardForm` tokenizes a card **without charging**. The `onSuccess` callback receives a payload your **backend** exchanges for a permanent **`cardId`** (used later with `usePOK`).

**Flow:** `AddCardForm` → `onSuccess(AddCardData)` → **your** `POST /api/cards` → server calls **Tokenize Card API** → store `cardId` on the customer.

```jsx
import { AddCardForm } from "@nebula-ltd/pok-payments-js/react";
import { PaymentErrorResponse, AddCardData } from "@nebula-ltd/pok-payments-js";

function SaveCardPage() {
  const handleSuccess = (cardPayload: AddCardData) => {
    fetch("/api/cards", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(cardPayload)
    });
  };

  return (
    <AddCardForm
      onSuccess={handleSuccess}
      onError={(error: PaymentErrorResponse) => console.error(error)}
      buttonTitle="Save card"
      options={{ env: "staging", locale: "en", countrySelect: "modal" }}
    />
  );
}
```

> [!warning]
> `/api/cards` is an **example** path you implement. Exchange the payload immediately via the [Tokenize Card API](https://payments.doc.pokpay.io/#741b843c-a3e9-4dc6-afad-e575e7ecc7b4) on the server — do not store raw card numbers or long-lived JWE in your database.

### Props

| Prop          | Type                                    | Required | Description                                                    |
| -------------- | ------------------------------------------| ---------- | ------------------------------------------------------------------|
| `onSuccess`   | `(cardPayload: AddCardData) => void`    | Yes      | Receives tokenized payload. Forward to your backend.           |
| `onError`     | `(error: PaymentErrorResponse) => void` | No       | Fires on validation, encryption, or tokenization failure.      |
| `buttonTitle` | `string`                                | No       | Submit button label. Defaults to `"Add Card"`.                 |
| `options`     | `object`                                | No       | Same `env` / `locale` / `initialState` as `GuestCheckoutForm`. |

---

## Pay with saved card

`usePOK` charges a **previously tokenized** card (`cardId` from [Save a card](#save-a-card)). Your backend must create a **new SDK order** for each payment and run **3-D Secure setup** before the user taps Pay.

**Save then pay (end-to-end):**

1. **Save (earlier):** `AddCardForm` → your `/api/cards` → store `cardId`.
2. **Checkout (now):** Server creates SDK order → `orderId`.
3. **On Pay:** Server `setup-tokenized-3ds` for `{cardId}` + `{orderId}` → returns `payerAuthentication`.
4. **Client:** `usePOK(orderId, …)` → `payByCardToken(payerAuthentication)`.

> [!danger]
> **The following two steps must run on your server — never in the browser.** `keyId` and `keySecret` must never appear in client-side code. The browser only ever receives the resulting `payerAuthentication` object.

### Step 1 — Backend: authenticate and set up 3-DS

Your backend must make two API calls. Use `api-staging.pokpay.io` when testing.

**Authenticate**

```http
POST https://api.pokpay.io/auth/sdk/login
Content-Type: application/json

{
  "keyId": "YOUR_KEY_ID",
  "keySecret": "YOUR_KEY_SECRET"
}
---
{
  "statusCode": 200,
  "data": {
    "accessToken": "eyJ...",
    "refreshToken": "...",
    "expiresIn": 3600,
    "tokenType": "Bearer"
  }
}
```

**Set up 3-D Secure for the saved card**

```http
POST https://api.pokpay.io/credit-debit-cards/{cardId}/setup-tokenized-3ds
Content-Type: application/json
Authorization: Bearer {accessToken}

{
  "sdkOrder": { "id": "ORDER_ID" }
}
---
{
  "statusCode": 200,
  "data": {
    "payerAuthentication": { "threeDSServerTransID": "...", "acsURL": "..." }
  }
}
```

`ORDER_ID` must be the same `orderId` you pass to `usePOK`. Use the `cardId` you stored when the user saved the card.

Expose an endpoint (e.g. POST /api/prepare-token-payment) that runs both calls and returns data.payerAuthentication to the frontend. Call it when the user taps Pay

### Step 2 — Frontend: usePOK hook

```jsx
import { useState } from "react";
import { usePOK } from "@nebula-ltd/pok-payments-js/react";
import { PaymentErrorResponse } from "@nebula-ltd/pok-payments-js";

interface Props {
  orderId: string;
  payerAuthentication: object;
}

function PayButton({ orderId, payerAuthentication }: Props) {
  const [loading, setLoading] = useState(false);
  const [errorMsg, setErrorMsg] = useState("");

  const { payByCardToken } = usePOK(
    orderId,
    () => {
      setLoading(false);
      window.location.href = "/order-confirmed";
    },
    (error: PaymentErrorResponse) => {
      setLoading(false);
      setErrorMsg(error.message ?? "Payment failed. Please try again.");
    },
    "staging"
  );

  return (
    <>
      <button
        onClick={() => { setLoading(true); payByCardToken(payerAuthentication); }}
        disabled={loading}>
        {loading ? "Processing…" : "Pay now"}
      </button>
      {errorMsg && <p role="alert">{errorMsg}</p>}
    </>
  );
}
```

> [!note]
> Pass the **`payerAuthentication`** object to `payByCardToken` exactly as returned by your backend — not the whole HTTP response wrapper.

### usePOK parameters

| Parameter     | Type                                    | Required | Description                                    |
| --------------- | ------------------------------------------| ---------- | -------------------------------------------------|
| `orderId`     | `string`                                | Yes      | Must match the ID used in the 3-DS setup call. |
| `onSuccess`   | `() => void`                            | Yes      | Fires after capture.                           |
| `onError`     | `(error: PaymentErrorResponse) => void` | Yes      | Fires on 3-DS or capture failure.              |
| `environment` | `'production' \| 'staging'`             | No       | Defaults to `'production'`.                    |

---

## Low-level encryption

`encryptCard()` returns a short-lived JWE token from raw card data. Use this when you build your own card input UI instead of using the form components.

```jsx
import { encryptCard } from "@nebula-ltd/pok-payments-js";

const token = await encryptCard({
  cardNumber: "4242424242424242",
  expiration: "12/28",
  securityCode: "123",
  env: "staging"
});

await fetch("/api/tokenize-card", {
  method: "POST",
  headers: { "Content-Type": "application/json" },
  body: JSON.stringify({ token })
});
```

> [!warning]
> The JWE is single-use and short-lived. Your backend must exchange it immediately via the [Tokenize Card API](https://payments.doc.pokpay.io/#741b843c-a3e9-4dc6-afad-e575e7ecc7b4). Do not store the JWE itself.

---

## Customizing styles

See [POK Payments JS — Customizing styles](/pok-js#customizing-styles).

---

## Error handling

All callbacks receive a `PaymentErrorResponse` with a `message` field. Never show the raw message to end users — log it server-side and display a friendly retry prompt.

- **Invalid / expired card** — "Please check your card details."
- **3-D Secure rejected** — user must contact their bank or use another card.
- **Order already paid / not found** — recreate the order on your backend.
- **Network failure** — the SDK is idempotent against the same order ID. Safe to retry.

---

## Going to production

1. Remove test card numbers from `initialState`.
2. Switch `env` to `'production'` and confirm your backend targets `api.pokpay.io`.
3. Smoke-test with a real card on a low-value transaction.
4. Confirm `PaymentErrorResponse` payloads are captured in your server-side logs.

---

# Vanilla JS (npm)

> Use @nebula-ltd/pok-payments-js without React — encryptCard() for guest checkout, card tokenization, and saved-card flows with your own UI.

# Vanilla JS (npm)

Use `@nebula-ltd/pok-payments-js` without React. This path gives you `encryptCard()` — a low-level encryption primitive you combine with your own UI and backend calls to build any payment flow.

```bash
npm install @nebula-ltd/pok-payments-js
```

No peer dependencies. TypeScript types are bundled.

---

## Base URLs (staging vs production)

Your **backend** calls the REST API on one of these hosts. Pass matching `env` to every `encryptCard()` call.

| Environment    | API base URL                    | `encryptCard` `env`      | Credentials                      |
| --------------- | ---------------------------------- | --------------------------- | ------------------------------------|
| **Staging**    | `https://api-staging.pokpay.io` | `'staging'`              | Staging `keyId` / `keySecret`    |
| **Production** | `https://api.pokpay.io`         | `'production'` (default) | Production `keyId` / `keySecret` |

> [!warning]
> **Do not mix environments.** Use [test cards](/pok-js#test-cards) only on **staging**.

---

## Choose your integration

| Goal                        | Vanilla JS API                           | Your backend must                                                                                                       |
| ----------------------------- | ------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------- |
| Pay once (new card)         | `encryptCard` + your checkout UI         | Create SDK order → tokenize JWE → `confirm` order ([REST API](/rest-api))                                               |
| Save card for later         | `encryptCard` + your save UI             | Accept JWE → [Tokenize Card API](https://payments.doc.pokpay.io/#741b843c-a3e9-4dc6-afad-e575e7ecc7b4) → store `cardId` |
| Pay with saved card         | Your Pay button → your API               | New SDK order + `setup-tokenized-3ds` + `confirm` (server-only)                                                         |
| Drop-in UI (no custom form) | Use [React](/react), [CDN](/cdn) instead | Same backend responsibilities                                                                                           |

> [!note]
> **Server vs browser:** `keyId` / `keySecret` and `POST .../sdk-orders` run **only on your server**. The browser only produces a short-lived **JWE** via `encryptCard()` — never merchant secrets.

---

## Guest checkout

Build your own card form, encrypt the data client-side, then hand the JWE token to your backend which creates the order and processes the charge.

> [!important]
> **Order creation and payment capture happen on your backend.** The browser only calls `encryptCard()` to produce a short-lived token. See the [REST API](/rest-api) on-ramp, [PHP SDK](/php-sdk), or [payments.doc.pokpay.io](https://payments.doc.pokpay.io/). Your API credentials must never appear in client-side code.

### Step 1 — Frontend: collect and encrypt card data

```js
import { encryptCard } from "@nebula-ltd/pok-payments-js";

async function handleCheckoutSubmit(formData) {
  let token;
  try {
    token = await encryptCard({
      cardNumber: formData.cardNumber,
      expiration: formData.expiration,
      securityCode: formData.cvv,
      env: "staging"
    });
  } catch (error) {
    showUserError("Could not process card. Please try again.");
    return;
  }

  const res = await fetch("/api/checkout", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ token, orderId: formData.orderId })
  });

  if (!res.ok) {
    showUserError("Payment failed. Please try again.");
    return;
  }

  window.location.href = "/order-confirmed";
}
```

> [!warning]
> The JWE token is single-use and short-lived. Pass it to your backend immediately and exchange it via the [Tokenize Card API](https://payments.doc.pokpay.io/#741b843c-a3e9-4dc6-afad-e575e7ecc7b4). Do not store the JWE itself.

### Step 2 — Backend: tokenize and charge

Your backend receives the JWE token from the frontend, exchanges it for a permanent card token via the [Tokenize Card API](https://payments.doc.pokpay.io/#741b843c-a3e9-4dc6-afad-e575e7ecc7b4), then captures the order with the returned `cardId`. Use `api-staging.pokpay.io` when testing.

**Capture the order using the tokenized card**

```http
POST https://api.pokpay.io/sdk-orders/{orderId}/confirm
Content-Type: application/json
Authorization: Bearer {accessToken}

{
  "cardId": "card_abc123"
}
---
{
  "statusCode": 200,
  "data": { "status": "CAPTURED" }
}
```

---

## Save a card

Encrypt card data and send it to your backend, which exchanges it for a permanent **`cardId`** to store against the customer (used later in [Pay with saved card](#pay-with-saved-card)).

**Flow:** `encryptCard` → **your** `POST /api/save-card` → server **Tokenize Card API** → store `cardId`.

### Frontend: encrypt and submit

```js
import { encryptCard } from "@nebula-ltd/pok-payments-js";

async function handleSaveCard(formData) {
  let token;
  try {
    token = await encryptCard({
      cardNumber: formData.cardNumber,
      expiration: formData.expiration,
      securityCode: formData.cvv,
      env: "staging"
    });
  } catch (error) {
    showUserError("Could not process card. Please try again.");
    return;
  }

  const res = await fetch("/api/save-card", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ token })
  });

  if (res.ok) {
    showSuccess("Card saved successfully.");
  } else {
    showUserError("Card could not be saved. Please try again.");
  }
}
```

> [!warning]
> `/api/save-card` is an **example** path you implement. Exchange the JWE immediately via the [Tokenize Card API](https://payments.doc.pokpay.io/#741b843c-a3e9-4dc6-afad-e575e7ecc7b4) on the server. Store the returned `id` as `cardId` on the customer record — do not store raw card numbers or long-lived JWE.

### Backend: tokenize and store

Your backend receives the JWE and exchanges it for a permanent card ID via the Tokenize Card API. Store the returned `id` against the customer record.

---

## Pay with saved card

For returning customers paying with a stored **`cardId`**, your backend handles the entire 3-DS setup and capture. The frontend only triggers your API and handles the result.

**Save then pay (end-to-end):**

1. **Save (earlier):** [Save a card](#save-a-card) → store `cardId`.
2. **Checkout (now):** Server creates SDK order → `orderId`.
3. **On Pay:** Server `setup-tokenized-3ds` + `confirm` for `{cardId}` + `{orderId}`.
4. **Page:** Call your `/api/pay-with-saved-card` endpoint.

> [!danger]
> **Both steps below must run on your server — never in the browser.** Your `keyId`, `keySecret`, and `accessToken` must never appear in client-side code.

### Backend: authenticate, set up 3-DS, and charge

Your backend makes three sequential API calls. Use `api-staging.pokpay.io` when testing.

**Authenticate**

```http
POST https://api.pokpay.io/auth/sdk/login
Content-Type: application/json

{
  "keyId": "YOUR_KEY_ID",
  "keySecret": "YOUR_KEY_SECRET"
}
---
{
  "statusCode": 200,
  "data": {
    "accessToken": "eyJ...",
    "tokenType": "Bearer",
    "expiresIn": 3600
  }
}
```

**Create SDK order** (new payment). See [REST API quick start](/rest-api#quick-start-prove-your-setup).

**Set up 3-D Secure**

```http
POST https://api.pokpay.io/credit-debit-cards/{cardId}/setup-tokenized-3ds
Content-Type: application/json
Authorization: Bearer {accessToken}

{
  "sdkOrder": { "id": "ORDER_ID" }
}
---
{
  "statusCode": 200,
  "data": { "payerAuthentication": { "threeDSServerTransID": "...", "acsURL": "..." } }
}
```

**Capture the order**

```http
POST https://api.pokpay.io/sdk-orders/{orderId}/confirm
Content-Type: application/json
Authorization: Bearer {accessToken}

{
  "cardId": "card_abc123"
}
---
{
  "statusCode": 200,
  "data": { "status": "CAPTURED" }
}
```

Use the `cardId` you stored when the user saved the card. `ORDER_ID` must match the SDK order for this checkout.

### Frontend: trigger and handle result

```js
async function payWithCard(cardId, orderId) {
  const res = await fetch("/api/pay-with-saved-card", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ cardId, orderId })
  });

  if (res.ok) {
    window.location.href = "/order-confirmed";
  } else {
    showUserError("Payment failed. Please try again or use a different card.");
  }
}
```

---

## encryptCard reference

```js
import { encryptCard } from "@nebula-ltd/pok-payments-js";

const token = await encryptCard({ cardNumber, expiration, securityCode, env });
```

### Parameters

| Parameter | Type | Required | Description |
|---|---|---|---|
| `cardNumber` | `string` | Yes | 16-digit, no spaces. Validates Visa, Mastercard, Maestro, Visa Electron. |
| `expiration` | `string` | Yes | `MM/YY`, must be a future date. |
| `securityCode` | `string` | Yes | CVV/CVC, 3 or 4 digits. |
| `env` | `'production' \| 'staging'` | No | Defaults to `'production'`. |

Returns `Promise<string>` — the JWE token string.

---

## Error handling

`encryptCard()` rejects on invalid input or network failure. Backend calls may fail with HTTP errors. Never show raw error messages to end users.

- **Invalid card number** — fails the Luhn check or unsupported brand.
- **Expired card** — expiration is in the past.
- **Network failure** — encryption is stateless; safe to retry.
- **Backend API errors** — log them server-side and surface a generic retry prompt.

---

## Going to production

1. Switch `env` to `'production'` in `encryptCard()`.
2. Confirm your backend uses production POK credentials and targets `api.pokpay.io`.
3. Remove any hardcoded test card numbers from your codebase.
4. Smoke-test with a real card on a low-value transaction.
5. Confirm `PaymentErrorResponse` payloads are captured in your server-side logs.

---

# CDN

> Add POK Payments to any HTML page with a single script tag — no npm, no build step, no framework required.

# CDN

Drop one script tag into any HTML page. No npm, no bundler, no framework required.

```html
<script src="https://static.pokpay.io/public/dist/pokpayments/pok-payment.js"></script>
```

Place it in `<head>` or at the end of `<body>`. The global `PokPayment` object is available as soon as the script loads and exposes four methods: `renderForm`, `setUpCardTokenPayment`, `renderAddCardForm`, and `encryptCard`.

---

## Base URLs (staging vs production)

Your **backend** calls the REST API on one of these hosts. Pass matching `env` in every `PokPayment.*` options object.

| Environment    | API base URL                    | `env` option             | Credentials                      |
| --------------- | ---------------------------------- | ---------------------------- | ------------------------------------|
| **Staging**    | `https://api-staging.pokpay.io` | `'staging'`              | Staging `keyId` / `keySecret`    |
| **Production** | `https://api.pokpay.io`         | `'production'` (default) | Production `keyId` / `keySecret` |

> [!warning]
> **Do not mix environments.** Use [test cards](/pok-js#test-cards) only on **staging**.

---

## Choose your integration

| Goal                | `PokPayment` API        | Your backend must                                                                                                           |
| -------------------- | -------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| Pay once (new card) | `renderForm`            | Create SDK order → pass `orderId` ([REST API](/rest-api), [PHP SDK](/php-sdk))                                              |
| Save card for later | `renderAddCardForm`     | Accept payload → [Tokenize Card API](https://payments.doc.pokpay.io/#741b843c-a3e9-4dc6-afad-e575e7ecc7b4) → store `cardId` |
| Pay with saved card | `setUpCardTokenPayment` | New SDK order + `setup-tokenized-3ds` → return `payerAuthentication`                                                        |
| Custom card inputs  | `encryptCard`           | Same tokenize exchange as save card                                                                                         |

> [!note]
> **Server vs browser:** `keyId` / `keySecret` and `POST .../sdk-orders` run **only on your server**. The page receives **`orderId`**, tokenized payloads, or **`payerAuthentication`** — never merchant secrets.

---

## Guest checkout

`PokPayment.renderForm` mounts a complete card checkout form into a target `<div>` and captures the payment on success.

> [!important]
> **Create the order on your backend first.** Call `POST /merchants/{merchantId}/sdk-orders` server-side and pass the returned order ID as the second argument. See the [PHP SDK](/php-sdk) or [REST API](https://payments.doc.pokpay.io/) docs. Never create orders from the browser — your API credentials must stay on the server.

```html
<!doctype html>
<html lang="en">
  <head>
    <meta charset="UTF-8" />
    <title>Checkout</title>
    <script src="https://static.pokpay.io/public/dist/pokpayments/pok-payment.js"></script>
  </head>
  <body>
    <div id="pok-checkout"></div>
    <button onclick="startPayment()">Pay now</button>

    <script>
      function startPayment() {
        PokPayment.renderForm(
          "pok-checkout",
          "YOUR_ORDER_ID",
          function onSuccess() {
            window.location.href = "/order-confirmed";
          },
          function onError(error) {
            console.error(error);
          },
          {
            env: "staging",
            locale: "en",
            initialState: {
              email: "customer@example.com",
              countryCode: "US"
            }
          }
        );
      }
    </script>
  </body>
</html>
```

### Parameters

| Parameter | Type | Required | Description |
|---|---|---|---|
| `containerId` | `string` | Yes | ID of the `<div>` where the form mounts. |
| `orderId` | `string` | Yes | SDK order ID from your backend. |
| `onSuccess` | `function` | No | Called after the payment is captured. |
| `onError` | `function` | No | Called with an error object on any failure. |
| `options` | `object` | No | `env`, `locale`, `initialState`. |

### options

| Field          | Type                        | Default        | Description                      |
| --------------- | ------------------------------| ---------------- | ------------------------------------|
| `env`          | `'production' \| 'staging'` | `'production'` | Use `'staging'` with test cards. |
| `locale`       | `'en' \| 'it' \| 'al'`      | `'en'`         | Form language.                   |
| `initialState` | `object`                    | —              | Pre-fills form fields.           |

### initialState fields

| Field                | Type     | Description                                          |
| --------------------- | ---------- | ------------------------------------------------------|
| `cardNumber`         | `string` | Visa, Visa Electron, Mastercard, Maestro.            |
| `email`              | `string` | Customer email.                                      |
| `expiration`         | `string` | `MM/YY`, future date.                                |
| `securityCode`       | `string` | CVV/CVC, max 3 characters.                           |
| `holdersName`        | `string` | Name as it appears on the card.                      |
| `countryCode`        | `string` | ISO 3166-1 alpha-2. Determines address fields shown. |
| `address1`           | `string` | Street address line 1.                               |
| `locality`           | `string` | City (auto-shown for US/CA).                         |
| `administrativeArea` | `string` | State or province code.                              |
| `postalCode`         | `string` | Postal or ZIP code.                                  |
| `phoneNumber`        | `string` | E.164 format.                                        |

---

## Save a card

`PokPayment.renderAddCardForm` tokenizes a card **without charging**. The success callback receives a payload your **backend** exchanges for a permanent **`cardId`** (used later with `setUpCardTokenPayment`).

**Flow:** `renderAddCardForm` → `onSuccess(cardPayload)` → **your** `POST /api/cards` → server calls **Tokenize Card API** → store `cardId` on the customer.

```html
<div id="pok-add-card"></div>
<button onclick="startAddCard()">Save card</button>

<script>
  function startAddCard() {
    PokPayment.renderAddCardForm(
      'pok-add-card',
      'Save card',
      function onSuccess(cardPayload) {
        fetch('/api/cards', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(cardPayload)
        });
      },
      function onError(error) {
        console.error(error);
      },
      { env: 'staging', locale: 'en' }
    );
  }
</script>
```

> [!warning]
> `/api/cards` is an **example** path you implement. Exchange the payload immediately via the [Tokenize Card API](https://payments.doc.pokpay.io/#741b843c-a3e9-4dc6-afad-e575e7ecc7b4) on the server — do not store raw card numbers or long-lived JWE in your database.

### Parameters

| Parameter     | Type       | Required | Description                                              |
| --------------- | ------------| ---------- | ------------------------------------------------------------|
| `containerId` | `string`   | Yes      | ID of the `<div>` where the form mounts.                 |
| `buttonTitle` | `string`   | Yes      | Submit button label.                                     |
| `onSuccess`   | `function` | Yes      | Receives the tokenized payload. Forward to your backend. |
| `onError`     | `function` | No       | Called on any failure.                                   |
| `options`     | `object`   | No       | Same `env` / `locale` / `initialState` as `renderForm`.  |

---

## Pay with saved card

`PokPayment.setUpCardTokenPayment` charges a **previously tokenized** card (`cardId` from [Save a card](#save-a-card)). Your backend must create a **new SDK order** for each payment and run **3-D Secure setup** before the user taps Pay.

**Save then pay (end-to-end):**

1. **Save (earlier):** `renderAddCardForm` → your `/api/cards` → store `cardId`.
2. **Checkout (now):** Server creates SDK order → `orderId`.
3. **On Pay:** Server `setup-tokenized-3ds` for `{cardId}` + `{orderId}` → returns `payerAuthentication`.
4. **Page:** `setUpCardTokenPayment` with that object.

> [!danger]
> **The following two steps must run on your server — never in the browser.** Your `keyId` and `keySecret` must never appear in client-side code. The browser only ever receives the resulting `payerAuthentication` object.

### Step 1 — Backend: authenticate and set up 3-DS

Your backend must make two API calls. Use `api-staging.pokpay.io` when testing.

**Authenticate**

```http
POST https://api.pokpay.io/auth/sdk/login
Content-Type: application/json

{
  "keyId": "YOUR_KEY_ID",
  "keySecret": "YOUR_KEY_SECRET"
}
---
{
  "statusCode": 200,
  "data": {
    "accessToken": "eyJ...",
    "refreshToken": "...",
    "expiresIn": 3600,
    "tokenType": "Bearer"
  }
}
```

**Set up 3-D Secure for the saved card**

```http
POST https://api.pokpay.io/credit-debit-cards/{cardId}/setup-tokenized-3ds
Content-Type: application/json
Authorization: Bearer {accessToken}

{
  "sdkOrder": { "id": "ORDER_ID" }
}
---
{
  "statusCode": 200,
  "data": {
    "payerAuthentication": { "threeDSServerTransID": "...", "acsURL": "..." }
  }
}
```

`ORDER_ID` must match the `orderId` passed to `setUpCardTokenPayment`. Use the `cardId` you stored when the user saved the card.

Expose an endpoint (e.g. POST /api/prepare-token-payment) that runs both calls and returns data.payerAuthentication to the frontend. Call it when the user taps Pay.

### Step 2 — Frontend: setUpCardTokenPayment

```html
<div id="pay-by-token"></div>
<button onclick="processPayment()">Pay now</button>

<script>
  async function processPayment() {
    const payerAuth = await fetch("/api/prepare-token-payment", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ cardId: "CARD_ID", orderId: "ORDER_ID" })
    }).then(function(r) { return r.json(); });

    PokPayment.setUpCardTokenPayment({
      containerId: "pay-by-token",
      orderId: "ORDER_ID",
      payerAuthentication: payerAuth,
      onSuccess: function() { window.location.href = "/order-confirmed"; },
      onError: function(error) { console.error(error); },
      env: "staging"
    });
  }
</script>
```

### Options

| Option                | Type                        | Required | Description                                            |
| ----------------------- | ------------------------------| ---------- | ----------------------------------------------------------|
| `containerId`         | `string`                    | Yes      | ID of the `<div>` where the 3-DS challenge mounts.     |
| `orderId`             | `string`                    | Yes      | Must match the ID used in the backend 3-DS setup call. |
| `payerAuthentication` | `object`                    | Yes      | Unmodified object from your backend.                   |
| `onSuccess`           | `function`                  | No       | Fires when capture succeeds.                           |
| `onError`             | `function`                  | No       | Fires on any 3-DS or capture failure.                  |
| `env`                 | `'production' \| 'staging'` | No       | Defaults to `'production'`.                            |

---

## Low-level encryption

`PokPayment.encryptCard` encrypts raw card details into a short-lived JWE token. Use this when you collect card data in your own UI and only need the encryption primitive without a form.

```html
<script>
  async function encryptAndSend(cardNumber, expiration, cvv) {
    try {
      const token = await PokPayment.encryptCard({
        cardNumber: cardNumber,
        expiration: expiration,
        securityCode: cvv,
        env: "staging"
      });

      await fetch("/api/tokenize-card", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ token })
      });
    } catch (error) {
      console.error("Encryption failed:", error);
    }
  }
</script>
```

> [!warning]
> The JWE is single-use and short-lived. Your backend must exchange it immediately via the [Tokenize Card API](https://payments.doc.pokpay.io/#741b843c-a3e9-4dc6-afad-e575e7ecc7b4). Do not store the JWE itself.

### Parameters

| Parameter      | Type                        | Required | Description                 |
| ---------------- | ------------------------------| ---------- | ------------------------------|
| `cardNumber`   | `string`                    | Yes      | 16-digit, no spaces.        |
| `expiration`   | `string`                    | Yes      | `MM/YY`, future date.       |
| `securityCode` | `string`                    | Yes      | CVV/CVC, 3 or 4 digits.     |
| `env`          | `'production' \| 'staging'` | No       | Defaults to `'production'`. |

---

## Customizing styles

See [POK Payments JS — Customizing styles](/pok-js#customizing-styles).

---

## Error handling

Error callbacks receive an object with a `message` field. Never show the raw message to end users — log it and show a generic retry prompt.

- **Invalid / expired card** — "Please check your card details."
- **3-D Secure rejected** — user must contact their bank or use another card.
- **Order already paid / not found** — recreate the order on your backend.
- **Network failure** — the SDK is idempotent against the same order ID. Safe to retry.

---

## Going to production

1. Remove test card numbers from `initialState`.
2. Switch `env` to `'production'` and confirm your backend targets `api.pokpay.io`.
3. Smoke-test with a real card on a low-value transaction.
4. Confirm error payloads are captured in your server-side logs.

---

# React Native

> Drop-in components and primitives for accepting card payments in React Native apps — from @nebula-ltd/pok-payments-rn.

# React Native

`@nebula-ltd/pok-payments-rn` is the React Native counterpart to `@nebula-ltd/pok-payments-js`. Both SDKs share the same POK backend API but differ in how they present the payment UI.

```bash
npm install @nebula-ltd/pok-payments-rn
# or
yarn add @nebula-ltd/pok-payments-rn
```

The SDK gives you three layers of integration depth:

| Layer                 | What it does                                                                | When to use                                                                      |
| ----------------------- | -------------------------------------------------------------------------------| ------------------------------------------------------------------------------------|
| **Composed surfaces** | Drop-in components that run the full payment flow end-to-end.               | Most integrations — working payment in a few lines.                              |
| **Imperative API**    | `PokPayments.payByToken(...)` — headless payment with a pre-tokenized card. | You have your own payment screen and just need to trigger the charge.            |
| **Primitives**        | `encryptCard` and `createChallenge` as standalone subpath exports.          | You have an existing flow and only need native JWE encryption and 3DS mechanics. |

> [!important]
> **Custom dev build required.** The SDK uses custom native modules and cannot run on Expo Go. Expo users should use `eas build` with a development client.

---

## Requirements

|              | Minimum                  |
| -------------- | ---------------------------|
| React        | 18.0                     |
| React Native | 0.73                     |
| iOS          | 13.0 (Swift, CocoaPods)  |
| Android      | minSdkVersion 24, Kotlin |

### iOS setup

After installing the package, run:

```bash
cd ios && pod install && cd ..
```

This pulls in [JOSESwift](https://github.com/airsidemobile/JOSESwift) for JWE encryption. If your project targets below iOS 13, bump the deployment target in `ios/Podfile`:

```ruby
platform :ios, '13.0'
```

### Android setup

Autolinking handles native module registration. After installing, rebuild:

```bash
cd android && ./gradlew clean && cd ..
npx react-native run-android
```

If you use **ProGuard / R8**, add these keep rules to `proguard-rules.pro`:

```
-keep class com.nimbusds.jose.** { *; }
-keep class net.jcip.annotations.** { *; }
```

---

## Concepts

### Environment

Pass `env` to each surface at runtime — no `.env` files needed.

| `env`                      | Base URL                         |
| ----------------------------- | ------------------------------------|
| `'staging'`                | `https://api-staging.pokpay.io/` |
| `'production'` _(default)_ | `https://api.pokpay.io/`         |

```ts
const env = __DEV__ ? 'staging' : 'production';
```

### Locale

| `locale`           | Language |
| --------------------- | ----------|
| `'en'` _(default)_ | English  |
| `'it'`             | Italian  |
| `'al'`             | Albanian |

Pass a `messages` prop to override individual strings or supply a new language entirely.

### Orders

Most surfaces take an `orderId` — a UUID from your backend's order creation endpoint. Create the order server-side before opening the payment screen.

### Payer authentication

For pay-by-token flows, pass the `PayerAuthentication` object returned by your backend:

```ts
interface PayerAuthentication {
  deviceDataCollection?: { url: string; accessToken: string };
  creditDebitCard: { id: string };
  payerAuthSetupReferenceId: string;
}
```

A `VisaTerminalPayerAuthentication` variant also exists for Visa Terminal (procard) flows:

```ts
interface VisaTerminalPayerAuthentication {
  creditDebitCard: { id: string };
  paymentFlowId: string;
  hasTerminal: boolean;
}
```

`PokPayments.payByToken()` accepts either shape transparently.

---

## Choose your integration

| Goal                | React Native API           | Your backend must                                                                                                           |
| -------------------- | ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| Pay once (new card) | `<GuestCheckout />`        | Create SDK order → pass `orderId` ([REST API](/rest-api), [PHP SDK](/php-sdk))                                              |
| Save card for later | `<AddCardForm />`          | Accept payload → [Tokenize Card API](https://payments.doc.pokpay.io/#741b843c-a3e9-4dc6-afad-e575e7ecc7b4) → store `cardId` |
| Pay with saved card | `PokPayments.payByToken()` | New SDK order + `setup-tokenized-3ds` → return `payerAuthentication`                                                        |
| Custom card inputs  | `encryptCard` (subpath)    | Same tokenize exchange as save card                                                                                         |

> [!note]
> **Server vs app:** `keyId` / `keySecret` and `POST .../sdk-orders` run **only on your server**. The app receives **`orderId`**, **`AddCardData`**, or **`payerAuthentication`** — never merchant secrets. Use **`env: 'staging'`** with `api-staging.pokpay.io` and [test cards](/pok-js#test-cards) while developing.

---

## Guest checkout

`<GuestCheckout />` is a full-form payment component. It collects card and billing details, encrypts them natively, runs Cybersource device data collection, presents the 3DS challenge modal if the bank requires step-up, and confirms the order.

> [!important]
> **Create the order on your backend first.** Call `POST /merchants/{merchantId}/sdk-orders` server-side and pass the returned `orderId` as a prop. See the [REST API](/rest-api) on-ramp, [PHP SDK](/php-sdk), or [payments.doc.pokpay.io](https://payments.doc.pokpay.io/). Your API credentials must never appear in client-side code.

```tsx
import { GuestCheckout } from '@nebula-ltd/pok-payments-rn';

<GuestCheckout
  orderId={orderId}
  env="staging"
  locale="en"
  onSuccess={() => navigation.navigate('Success')}
  onError={(err) => Alert.alert(`Error (${err.code})`, err.message)}
/>;
```

### Props

| Prop            | Type                            | Default        | Description                                                                 |
| ----------------- | ---------------------------------- | ---------------- | -----------------------------------------------------------------------------|
| `orderId`       | `string`                        | _required_     | UUID from your backend's order creation endpoint.                           |
| `env`           | `'staging' \| 'production'`     | `'production'` | Target POK environment.                                                     |
| `locale`        | `'en' \| 'it' \| 'al'`          | `'en'`         | Form language.                                                              |
| `theme`         | `'light' \| 'dark'`             | `'light'`      | Color theme.                                                                |
| `showAmount`    | `boolean`                       | `false`        | Display the order total above the submit button.                            |
| `initialValues` | `Partial<CardAndBillingFields>` | `{}`           | Pre-fill card number, expiry, CVV, email, address, etc. Useful for testing. |
| `onSuccess`     | `() => void`                    | —              | Called when the order is confirmed.                                         |
| `onError`       | `(error: PokError) => void`     | —              | Called when the flow fails.                                                 |
| `styles`        | `PokStyleOverrides`             | —              | Visual overrides — see [Theming](#theming).                                 |
| `messages`      | `PartialMessages`               | —              | Custom localized strings — see [Localization](#localization).               |

---

## Save a card

`<AddCardForm />` tokenizes a card **without charging**. The `onSuccess` callback receives a payload your **backend** exchanges for a permanent **`cardId`** (used later with `payByToken`).

**Flow:** `<AddCardForm />` → `onSuccess(payload)` → **your** `POST /api/cards` → server calls **Tokenize Card API** → store `cardId` on the customer.

```tsx
import { AddCardForm } from '@nebula-ltd/pok-payments-rn';

<AddCardForm
  env="staging"
  onSuccess={(payload) => {
    fetch('/api/cards', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });
  }}
  onError={(err) => Alert.alert(err.code, err.message)}
/>;
```

> [!warning]
> `/api/cards` is an **example** path you implement. Exchange the payload immediately via the [Tokenize Card API](https://payments.doc.pokpay.io/#741b843c-a3e9-4dc6-afad-e575e7ecc7b4) on the server — do not store raw card numbers or long-lived JWE in your database.

### Props

| Prop            | Type                             | Default        | Description                                              |
| ----------------- | ----------------------------------- | ---------------- | -------------------------------------------------------------|
| `env`           | `'staging' \| 'production'`      | `'production'` |                                                          |
| `locale`        | `'en' \| 'it' \| 'al'`           | `'en'`         |                                                          |
| `theme`         | `'light' \| 'dark'`              | `'light'`      |                                                          |
| `buttonTitle`   | `string`                         | `'Add card'`   | Submit button label.                                     |
| `initialValues` | `Partial<CardAndBillingFields>`  | `{}`           |                                                          |
| `onSuccess`     | `(payload: AddCardData) => void` | _required_     | Receives the encrypted payload. Forward to your backend. |
| `onError`       | `(error: PokError) => void`      | —              |                                                          |
| `styles`        | `PokStyleOverrides`              | —              |                                                          |
| `messages`      | `PartialMessages`                | —              |                                                          |

### AddCardData shape

```ts
interface AddCardData {
  csFlexCard: { jwe: string };
  billingInfo: {
    firstName: string;
    lastName: string;
    email: string;
    countryCode: string;
    administrativeArea: string;
    locality: string;
    address1: string;
    postalCode: string;
    phoneNumber: string;
  };
  securityCode: string;
}
```

---

## Pay with saved card

`PokPayments.payByToken()` charges a **previously tokenized** card (`cardId` from [Save a card](#save-a-card)). Your backend must create a **new SDK order** for each payment and run **3-D Secure setup** before you call this function.

**Save then pay (end-to-end):**

1. **Save (earlier):** `<AddCardForm />` → your `/api/cards` → store `cardId`.
2. **Checkout (now):** Server creates SDK order → `orderId`.
3. **On Pay:** Server `setup-tokenized-3ds` for `{cardId}` + `{orderId}` → returns `payerAuthentication`.
4. **App:** `PokPayments.payByToken({ orderId, payerAuth })`.

> [!danger]
> **The backend steps below must run on your server — never in the app.** Your `keyId` and `keySecret` must never appear in client-side code. The app only ever receives the resulting `payerAuthentication` object.

### Step 1 — Backend: authenticate and set up 3-DS

Your backend must make these API calls. Use `api-staging.pokpay.io` when testing.

**Authenticate**

```http
POST https://api.pokpay.io/auth/sdk/login
Content-Type: application/json

{
  "keyId": "YOUR_KEY_ID",
  "keySecret": "YOUR_KEY_SECRET"
}
---
{
  "statusCode": 200,
  "data": {
    "accessToken": "eyJ...",
    "refreshToken": "...",
    "expiresIn": 3600,
    "tokenType": "Bearer"
  }
}
```

**Create SDK order** (new payment — same as guest checkout). See [REST API quick start](/rest-api#quick-start-prove-your-setup).

**Set up 3-D Secure for the saved card**

```http
POST https://api.pokpay.io/credit-debit-cards/{cardId}/setup-tokenized-3ds
Content-Type: application/json
Authorization: Bearer {accessToken}

{
  "sdkOrder": { "id": "ORDER_ID" }
}
---
{
  "statusCode": 200,
  "data": {
    "payerAuthentication": { "threeDSServerTransID": "...", "acsURL": "..." }
  }
}
```

`ORDER_ID` must match the `orderId` passed to `payByToken`. Use the `cardId` you stored when the user saved the card.

Expose an endpoint (e.g. POST /api/prepare-token-payment) that runs both calls and returns data.payerAuthentication to the frontend. Call it when the user taps Pay

### Step 2 — App: payByToken

```ts
import { PokPayments } from '@nebula-ltd/pok-payments-rn';

async function pay(orderId: string) {
  const payerAuth = await fetch('/api/prepare-token-payment', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ cardId: 'CARD_ID', orderId })
  }).then((r) => r.json());

  try {
    await PokPayments.payByToken({ orderId, payerAuth, env: 'staging' });
    navigation.navigate('Success');
  } catch (err) {
    Alert.alert(`Error (${err.code})`, err.message);
  }
}
```

### Options

| Field       | Type                                                     | Default        | Description                                            |
| ------------- | ------------------------------------------------------------| ---------------- | -----------------------------------------------------------|
| `orderId`   | `string`                                                 | _required_     | Must match the ID used in the backend 3-DS setup call. |
| `payerAuth` | `PayerAuthentication \| VisaTerminalPayerAuthentication` | _required_     | From your backend's card lookup.                       |
| `env`       | `'staging' \| 'production'`                              | `'production'` |                                                        |
| `locale`    | `'en' \| 'it' \| 'al'`                                   | `'en'`         |                                                        |
| `messages`  | `PartialMessages`                                        | —              | Custom localized strings for error messages.           |

**Visa Terminal.** If `payerAuth` is a `VisaTerminalPayerAuthentication` with `hasTerminal: true`, `payByToken` automatically takes the procard 3DS path — no branching needed in your code.

---

## Low-level encryption

`encryptCard` performs native JWE encryption (RSA-OAEP + A256GCM via JOSESwift on iOS and nimbus-jose-jwt on Android). Use this when you have your own card form UI and only need the encryption primitive.

```ts
import { encryptCard } from '@nebula-ltd/pok-payments-rn/encryption';
import axios from 'axios';

async function saveCard(number: string, expiration: string, cvv: string) {
  const { data: keys } = await axios.get('/my-backend/flex-encryption-key');

  const jwe = await encryptCard(
    { number, expiration, securityCode: cvv },
    keys
  );

  return axios.post('/my-backend/cards', { csFlexCard: { jwe } });
}
```

`encryptCard` signature:

```ts
encryptCard(
  card: { number: string; expiration: string; securityCode: string },
  keys: EncryptionContext
): Promise<string>
```

`expiration` accepts `MM/YY` — slashes are optional; the function strips non-digits internally. `keys` is the Flex encryption context returned by your backend's encryption-key endpoint.

A second function, `encryptProCard(cardNumber, procardKey)`, is exported for Visa Terminal flows.

---

## 3DS challenge primitive

`createChallenge` returns a configured instance for running Cybersource DDC and 3DS step-up challenges in native WebViews. Use this with your own backend for full control over the authentication flow.

```ts
import { createChallenge } from '@nebula-ltd/pok-payments-rn/challenge';

const challenge = createChallenge({
  cybersource: {
    orgId: YOUR_CYBERSOURCE_ORG_ID,
    merchantId: YOUR_CYBERSOURCE_MERCHANT_ID,
  },
  socket: {
    baseUrl: 'https://api.yourbackend.com/',
  },
  env: 'staging',
});
```

**Device data collection** (invisible native WebView, resolves with a session ID):

```ts
const sessionId = await challenge.collectDeviceData({
  url: ddc.url,
  accessToken: ddc.accessToken,
});
```

**3DS step-up** (native full-screen modal, resolves on successful authentication):

```ts
if (enrollment.status === 'PENDING_AUTHENTICATION') {
  await challenge.runChallenge({
    mode: 'standard',
    cardId,
    stepUpUrl: enrollment.stepUp.url,
    accessToken: enrollment.stepUp.accessToken,
    MD: enrollment.MD ?? '',
  });
}
```

For Visa Terminal flows use `mode: 'procard'` with `orderId`, `acsUrl`, and `creq`. If your backend emits events with non-default names, pass `eventKeys: { success, failure }` to override.

---

## Error handling

Every SDK method rejects with a `PokError` — a typed error with a stable `code` and a localized `message`.

```ts
import { isPokError } from '@nebula-ltd/pok-payments-rn';

try {
  await PokPayments.payByToken({ orderId, payerAuth, env: 'staging' });
} catch (e) {
  if (isPokError(e)) {
    switch (e.code) {
      case 'CANCELLED':
      case 'AUTHENTICATION_FAILED':
      case 'TIMEOUT':
      case 'NETWORK_ERROR':
      case 'CARD_DECLINED':
      case 'VALIDATION_ERROR':
      case 'ENCRYPTION_ERROR':
      case 'UNKNOWN':
    }
  }
}
```

`e.cause` contains the underlying error when available; `e.httpStatus` contains the HTTP status code for API failures. Use `e.code` for branching and `e.message` for display — messages are localized to the `locale` passed to the surface.

---

## Theming

Composed surfaces accept a `theme` prop (`'light'` or `'dark'`) and a `styles` prop for fine-grained overrides. Style overrides merge on top of theme defaults — only specify the keys you want to change. The native 3DS modal is not themeable (system-default modal chrome) for security and consistency.

---

## Localization

Three locales are built-in: `en`, `it`, `al`. Override individual strings or replace the entire message map. `PartialMessages` is a deeply-optional version of the internal `Messages` type. Unspecified keys fall back to the built-in locale strings.

---

## Going to production

1. Switch `env` to `'production'` in all surfaces and primitives.
2. Confirm your backend targets `api.pokpay.io` (not staging).
3. Remove test card numbers from `initialValues`.
4. Smoke-test with a real card on a low-value transaction.
5. Confirm `PokError` payloads are captured in your server-side logs.

---

## Troubleshooting

**"Unable to resolve module `@nebula-ltd/pok-payments-rn/encryption`" in Metro**

Metro `exports` field support landed in 0.80. If you're on an older Metro, upgrade React Native to 0.73 or newer. The package also ships top-level fallback files (`encryption.js`, `challenge.js`) that older Metros can resolve via legacy resolution.

**iOS build fails with "JOSESwift not found"**

```bash
cd ios && pod cache clean --all && pod install && cd ..
```

**Android build fails with "Duplicate class com.nimbusds"**

```gradle
configurations.all {
  resolutionStrategy {
    force 'com.nimbusds:nimbus-jose-jwt:9.37.3'
  }
}
```

**"NativeChallengeModal is null" at runtime**

The native module failed to link. Rebuild from scratch and verify autolinking picked up the package:

```bash
npx react-native config | grep pok-payments
```

**3DS modal opens and closes immediately**

The socket event fired before the modal could present:

- Standard mode: backend must emit `${cardId}:successful` / `${cardId}:unsuccessful`
- Procard mode: backend must emit `${orderId}:order-confirmed` / `${orderId}:order-confirmation-failed`

Pass `eventKeys` in the `runChallenge` input to override event names.

---

# Flutter

> Flutter SDK for accepting card payments with native JWE encryption and natively presented 3DS challenges — pok_payments_flutter (Beta).

# Flutter

`pok_payments_flutter` is the Flutter counterpart to `@nebula-ltd/pok-payments-js` and `@nebula-ltd/pok-payments-rn`. All three SDKs share the same POK backend API and security model — they differ only in how the payment UI is presented.

```yaml
dependencies:
  pok_payments_flutter: ^0.0.1
```

```bash
flutter pub get
cd ios && pod install
```

The SDK gives you two layers of integration depth:

| Layer | What it does | When to use |
|---|---|---|
| **Composed widgets** | Drop-in widgets that run the full payment flow end-to-end. | Most integrations — working payment in a few lines. |
| **Imperative API** | `PokPayments.payByToken(...)` — headless payment with a pre-tokenized card. | You have your own payment screen and just need to trigger the charge. |

> [!important]
> **Custom dev build required.** The SDK ships native iOS and Android modules (JOSESwift / nimbus-jose-jwt for JWE, plus a native 3DS challenge surface). It will not run on Flutter web or in environments that strip native plugins.

---

## Requirements

| | Minimum |
|---|---|
| Flutter | 3.16 |
| Dart | 3.2 |
| iOS | 13.0 (Swift, CocoaPods) |
| Android | `minSdkVersion` 21, Kotlin |

### iOS setup

```bash
cd ios && pod install && cd ..
```

Pulls in [JOSESwift `~> 3.0`](https://github.com/airsidemobile/JOSESwift). If targeting below iOS 13, bump `platform :ios, '13.0'` in `ios/Podfile`.

### Android setup

The plugin transitively pulls in `com.nimbusds:nimbus-jose-jwt`, `androidx.appcompat:appcompat`, and `androidx.localbroadcastmanager`.

Register the native challenge `Activity` in `android/app/src/main/AndroidManifest.xml`, inside `<application>`:

```xml
<activity
    android:name="com.nebula.pok_payments_flutter.ChallengeActivity"
    android:theme="@style/Theme.AppCompat.Light.NoActionBar"
    android:exported="false" />
```

Then rebuild:

```bash
cd android && ./gradlew clean && cd ..
flutter run
```

R8/ProGuard keep rules:

```
-keep class com.nimbusds.jose.** { *; }
-keep class net.jcip.annotations.** { *; }
```

---

## Concepts

### Environment

| `Environment`                        | Base URL                         |
| --------------------------------------- | ------------------------------------|
| `Environment.staging`                | `https://api-staging.pokpay.io/` |
| `Environment.production` _(default)_ | `https://api.pokpay.io/`         |

```dart
final env = kReleaseMode ? Environment.production : Environment.staging;
```

### Locale

| `Locale`                | Language |
| --------------------------| ----------|
| `Locale.en` _(default)_ | English  |
| `Locale.it`             | Italian  |
| `Locale.al`             | Albanian |

### Orders

Most flows take an `orderId` — a UUID returned by your backend's order creation endpoint. Create the order **server-side** before opening the payment screen.

### Payer authentication

```dart
class PayerAuthentication {
  final String creditDebitCardId;
  final String payerAuthSetupReferenceId;
  final DeviceDataCollection? deviceDataCollection;
}

class VisaTerminalPayerAuthentication {
  final String creditDebitCardId;
  final String paymentFlowId;
  final bool hasTerminal;
}
```

`PokPayments.payByToken()` accepts either shape — the SDK picks the right path automatically based on the runtime type.

---

## Choose your integration

| Goal                | Flutter API                | Your backend must                                                                                                           |
| -------------------- | ----------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| Pay once (new card) | `GuestCheckout`            | Create SDK order → pass `orderId` ([REST API](/rest-api), [PHP SDK](/php-sdk))                                              |
| Save card for later | `AddCardForm`              | Accept payload → [Tokenize Card API](https://payments.doc.pokpay.io/#741b843c-a3e9-4dc6-afad-e575e7ecc7b4) → store `cardId` |
| Pay with saved card | `PokPayments.payByToken()` | New SDK order + `setup-tokenized-3ds` → return `payerAuthentication`                                                        |
| Custom card inputs  | `encryptCard` (if exposed) | Same tokenize exchange as save card                                                                                         |

---

## Guest checkout

`GuestCheckout` is a self-contained widget that collects card and billing details, encrypts them natively, runs Cybersource device data collection, presents the 3DS challenge modal if the bank requires step-up, and confirms the order.

```dart
import 'package:flutter/material.dart';
import 'package:pok_payments_flutter/pok_payments_flutter.dart';

class CheckoutScreen extends StatelessWidget {
  const CheckoutScreen({super.key, required this.orderId});
  final String orderId;

  @override
  Widget build(BuildContext context) => Scaffold(
        appBar: AppBar(title: const Text('Checkout')),
        body: GuestCheckout(
          env: Environment.staging,
          orderId: orderId,
          onSuccess: (result) => Navigator.of(context).pop(result),
          onError: (error) => ScaffoldMessenger.of(context).showSnackBar(
            SnackBar(content: Text('${error.code.name}: ${error.message}')),
          ),
        ),
      );
}
```

### Props

| Prop             | Type                           | Default            | Description                                              |
| ------------------ | --------------------------------| --------------------| --------------------------------------------------------------|
| `env`            | `Environment`                  | _required_         | `Environment.staging` or `Environment.production`.       |
| `orderId`        | `String`                       | _required_         | UUID from your backend's order creation endpoint.        |
| `onSuccess`      | `void Function(PaymentResult)` | _required_         | Called when the order is confirmed.                      |
| `onError`        | `void Function(PokError)`      | _required_         | Called when the flow fails.                               |
| `locale`         | `Locale`                       | `Locale.en`        | Form language.                                            |
| `themeMode`      | `ThemeMode`                    | `ThemeMode.system` | `light`, `dark`, or `system`.                              |
| `styleOverrides` | `PokStyleOverrides?`           | `null`              | Visual overrides.                                          |
| `showAmount`     | `bool`                         | `true`              | Append the fetched order amount to the Pay button label.  |

---

## Save a card

```dart
import 'package:pok_payments_flutter/pok_payments_flutter.dart';

AddCardForm(
  env: Environment.staging,
  onComplete: (data) {
    myApiClient.saveCard(
      jwe: data.csFlexCardJwe,
      billing: data.billing,
      securityCode: data.securityCode,
    );
  },
  onError: (error) => debugPrint('${error.code.name}: ${error.message}'),
);
```

### AddCardData shape

```dart
class AddCardData {
  final String csFlexCardJwe;
  final BillingAddress billing;
  final String securityCode;
}

class BillingAddress {
  final String firstName;
  final String lastName;
  final String country;
  final String city;
  final String postalCode;
  final String line1;
  final String? state;
  final String? email;
  final String? phone;
}
```

`BillingAddress.toJson()` emits the wire field names POK's API expects.

---

## Pay with saved card

```dart
import 'package:pok_payments_flutter/pok_payments_flutter.dart';

Future<PaymentResult> pay({
  required String orderId,
  required String creditDebitCardId,
  required String payerAuthSetupReferenceId,
}) {
  return PokPayments.payByToken(
    PayByTokenOptions(
      env: Environment.staging,
      orderId: orderId,
      payerAuth: PayerAuthentication(
        creditDebitCardId: creditDebitCardId,
        payerAuthSetupReferenceId: payerAuthSetupReferenceId,
      ),
    ),
  );
}
```

Backend steps (authenticate, create SDK order, setup-tokenized-3ds) mirror the React/RN flows exactly — same three endpoints, same shape.

---

## Error handling

```dart
import 'package:pok_payments_flutter/pok_payments_flutter.dart';

try {
  final result = await PokPayments.payByToken(options);
} catch (e) {
  if (isPokError(e)) {
    final pokError = e as PokError;
    switch (pokError.code) {
      case PokErrorCode.cancelled:
      case PokErrorCode.challengeDismissed:
      case PokErrorCode.challengeTimeout:
      case PokErrorCode.threeDsFailed:
      case PokErrorCode.cardDeclined:
      case PokErrorCode.networkError:
      case PokErrorCode.invalidCard:
      case PokErrorCode.invalidBilling:
      case PokErrorCode.encryptionFailed:
      case PokErrorCode.serverError:
      case PokErrorCode.authenticationFailed:
      case PokErrorCode.validationError:
      case PokErrorCode.unknown:
    }
  } else {
    rethrow;
  }
}
```

---

## Going to production

> [!warning]
> **Beta SDK — production usage is at your own risk.** Pin to an exact patch version, smoke-test on every upgrade, capture `PokError` in your crash reporter.

1. Switch `env` to `Environment.production` in every widget and options object.
2. Confirm your backend targets `api.pokpay.io` (not staging).
3. Remove any test card values from your code paths.
4. Verify your bundle ID / app ID is registered with POK for 3-D Secure.
5. Smoke-test with a real card on a low-value transaction.
6. Confirm `PokError` payloads are captured in your server-side and crash logs.

---

# PHP SDK

> Server-side PHP SDK for creating, capturing, and confirming POK Payments orders. Wraps the Checkout REST API with typed model classes.

# PHP SDK

`rpay/pokpay-payments-sdk` is the official PHP SDK for the POK Payments Checkout REST API. It wraps the HTTP endpoints in typed model classes so you can create, confirm, and capture orders without writing your own request layer.

Source: [pokpay-ltd/php-sdk on GitHub](https://github.com/pokpay-ltd/php-sdk).

## Requirements

- **PHP 7.3 or later.** Should also work on PHP 8.0+ but has not been formally tested.
- [GuzzleHttp](https://docs.guzzlephp.org/) — installed automatically as a dependency when you use Composer.
- POK API credentials (`keyId` and `keySecret`) issued from your POK merchant dashboard.

## Installation

### Composer (recommended)

```bash
composer require rpay/pokpay-payments-sdk
```

### Manual installation

```php
<?php
require_once('/path/to/pok-payments-sdk/vendor/autoload.php');
```

---

## Authentication

Every API call requires a JWT bearer token, obtained via `AuthApi::login()` with `keyId`/`keySecret`. Staging credentials work only against staging; production only against production.

```php
<?php
require_once(__DIR__ . '/vendor/autoload.php');

$config = RPay\POK\PaymentsSdk\Configuration::getDefaultConfiguration(true); // true=production, false=staging

$auth = new RPay\POK\PaymentsSdk\Api\AuthApi(
    $config,
    new GuzzleHttp\Client()
);

$keyId = 'your_key_id';
$keySecret = 'your_key_secret';

$payload = new RPay\POK\PaymentsSdk\Model\LoginSdkPayload($keyId, $keySecret);

try {
    $result = $auth->login($payload);
    $accessToken = $result->getData()->getAccessToken();
    print_r($accessToken);
} catch (Exception $e) {
    echo 'Login failed: ', $e->getMessage(), PHP_EOL;
}
```

> [!warning]
> **Where to store credentials.** Read `keyId` and `keySecret` from environment variables, a secrets manager, or your framework's config — never commit them. Refresh tokens via the `LoginResponseData::expiresAt` field; the SDK does not auto-refresh.

---

## End-to-end example: create, confirm, and capture an order

```php
<?php
require_once(__DIR__ . '/vendor/autoload.php');

use RPay\POK\PaymentsSdk\Configuration;
use RPay\POK\PaymentsSdk\Api\AuthApi;
use RPay\POK\PaymentsSdk\Api\MerchantsApi;
use RPay\POK\PaymentsSdk\Api\SdkOrdersApi;
use RPay\POK\PaymentsSdk\Model\LoginSdkPayload;
use RPay\POK\PaymentsSdk\Model\CreateSdkOrderPayload;

$config = Configuration::getDefaultConfiguration(false); // staging
$client = new GuzzleHttp\Client();

$auth = new AuthApi($config, $client);
$loginResult = $auth->login(new LoginSdkPayload(getenv('POK_KEY_ID'), getenv('POK_KEY_SECRET')));
$accessToken = $loginResult->getData()->getAccessToken();

$config->setAccessToken($accessToken);

$merchants = new MerchantsApi($config, $client);
$createPayload = new CreateSdkOrderPayload([
    'amount' => 1000,
    'currency' => 'EUR',
    'description' => 'Order #12345'
]);
$createResult = $merchants->createOrder('YOUR_MERCHANT_ID', $createPayload);
$sdkOrderId = $createResult->getData()->getId();

// Pass $sdkOrderId to your frontend, where the customer pays via @nebula-ltd/pok-payments-js.

$captureResult = $merchants->captureOrder('YOUR_MERCHANT_ID', $sdkOrderId);
print_r($captureResult);
```

For guest-checkout flows where the customer is unauthenticated and the frontend confirms the order directly, use `SdkOrdersApi::confirmOrderAsGuest()` instead of `MerchantsApi::captureOrder()`.

---

## API endpoints

All URIs are relative to `https://api.pokpay.io` in production, or `https://api-staging.pokpay.io` in staging.

| Class | Method | HTTP request | Description |
|---|---|---|---|
| `AuthApi` | `login` | **POST** `/auth/sdk/login` | Exchange `keyId` / `keySecret` for an access token. |
| `MerchantsApi` | `createOrder` | **POST** `/merchants/{merchantId}/sdk-orders` | Create a new SDK order. |
| `MerchantsApi` | `captureOrder` | **POST** `/merchants/{merchantId}/sdk-orders/{sdkOrderId}/capture` | Capture an authorized SDK order. |
| `SdkOrdersApi` | `getSdkOrderById` | **GET** `/sdk-orders/{sdkOrderId}` | Retrieve an order by ID. |
| `SdkOrdersApi` | `confirmOrder` | **POST** `/sdk-orders/{sdkOrderId}/confirm` | Confirm an authenticated order. |
| `SdkOrdersApi` | `confirmOrderAsGuest` | **POST** `/sdk-orders/{sdkOrderId}/guest-confirm` | Confirm a guest checkout order. |

---

## Models

- `ConfirmSdkOrderGuestPayload`
- `ConfirmSdkOrderPayload`
- `CreateSdkOrderPayload`
- `ErrorResponse`
- `FieldOfOperation`
- `LoginResponse`
- `LoginResponseData`
- `LoginSdkPayload`
- `Merchant`
- `SdkOrder`
- `SdkOrderProduct`
- `SdkOrderResponse`
- `SdkOrderResponseData`
- `SdkOrderSelf`
- `SdkOrderSplitWith`

(Full field reference for each: https://github.com/pokpay-ltd/php-sdk/tree/HEAD/docs/Model)

---

## Authorization

All authenticated endpoints use a JWT bearer token: `Authorization: Bearer <accessToken>`, sourced from `LoginResponseData::getAccessToken()`.

---

## Tests

```bash
composer install
vendor/bin/phpunit
```

Run against staging credentials before deploying changes that touch order creation or capture logic.

---

## Common errors

| Error | Meaning | Fix |
|---|---|---|
| `401 Unauthorized` on any non-login call | Access token is missing, malformed, or expired. | Re-call `AuthApi::login()` and update `Configuration::setAccessToken()`. |
| `403 Forbidden` on `createOrder` | Your `keyId` / `keySecret` is for a different merchant than the `merchantId` in the URL. | Use the correct credentials, or update the URL to match the credentials' merchant. |
| `404 Not Found` on `captureOrder` | The `sdkOrderId` doesn't exist or belongs to a different merchant. | Confirm the ID matches the order created by `createOrder`. |
| `409 Conflict` on `captureOrder` | The order has already been captured, or is in an unrecoverable state. | Don't retry blindly — fetch the order with `getSdkOrderById` and inspect its status first. |

---

# WooCommerce plugin

> Accept POK Payments in any WooCommerce store. Drop-in installer, configuration walkthrough, and go-live checklist.

# WooCommerce plugin

The POK Payments WooCommerce plugin adds a new payment gateway to any WooCommerce store. Customers see "POK" as a checkout option, complete the card flow on your site (no redirect), and the order moves to **Processing** when payment is captured.

## Requirements

- WooCommerce 6.0 or later
- WordPress 5.8 or later
- PHP 7.4 or later
- An active POK merchant account with API credentials (`keyId` / `keySecret`) issued from the POK dashboard
- HTTPS enabled on your storefront (required by 3-D Secure)

## Download

> **Latest stable:** `pokpaymentgateway-1.2.0.zip`

[Download the WooCommerce plugin](https://static.pokpay.io/public/dist/plugins/woocommerce/pokpaymentgateway-1.2.0.zip)

## Install

1. Log in to your WordPress admin dashboard.
2. Go to **Plugins → Add New → Upload Plugin**.
3. Choose `pokpaymentgateway-1.2.0.zip` and click **Install Now**.
4. Click **Activate Plugin** once installation finishes.

## Configure

1. Go to **WooCommerce → Settings → Payments**.
2. Find **POK Payments** in the list of gateways and toggle it on.
3. Click **Manage** (or **Set up**) to open the gateway configuration.
4. Fill in: **Title**, **Description**, **Environment** (Staging/Production), **Key ID**, **Key Secret** (stored encrypted in `wp_options`), **Merchant ID**.
5. Click **Save changes**.

> [!warning]
> **Never paste production credentials into a staging configuration, or vice versa.**

## Test

1. Add any product to the cart, go to checkout.
2. Select **POK Payments**.
3. Use a [test card number](/pok-js#test-cards): `4242 4242 4242 4242` (succeeds), any future `MM/YY`, any 3-digit CVV.
4. Place the order — should move to **Processing**, order notes show the POK SDK order ID.

## Go live

- [ ] Replace staging credentials with production `keyId` / `keySecret`.
- [ ] Toggle **Environment** to `Production`.
- [ ] Place a real low-value test order and refund it via the POK dashboard.
- [ ] Confirm HTTPS + valid certificate.
- [ ] Set up an alert on **WooCommerce → Status → Logs** for `pok-payments` errors.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| Gateway doesn't appear at checkout | Currency not supported, or gateway toggled off. | Check currency settings, re-enable gateway. |
| `401 Unauthorized` in logs | Credentials don't match selected environment. | Re-paste credentials, verify Staging vs Production. |
| Order stays **Pending payment** | Capture call failed silently. | Check WooCommerce logs; capture manually from POK dashboard if needed. |
| 3-D Secure challenge never appears | Storefront on HTTP, or bank doesn't require 3DS for that card. | Move to HTTPS; no challenge from the bank is expected behavior. |

## Updating

Deactivate and delete old version, upload new `.zip`. Settings persist between versions.

---

# PrestaShop plugin

> Accept POK Payments in PrestaShop. Plugins for both PrestaShop 1.7 and 1.6, with installation, configuration, and testing steps.

# PrestaShop plugin

Supports **PrestaShop 1.7+** and **PrestaShop 1.6** — separate plugin builds.

## Requirements

- PrestaShop 1.6.x or 1.7.x
- PHP 7.2+ (1.7) / PHP 5.6+ (1.6)
- Active POK merchant account with API credentials
- HTTPS enabled

## Downloads

| Store version | File |
|---|---|
| PrestaShop 1.7+ | `pokpaymentgateway-prestashop-1.7.zip` |
| PrestaShop 1.6 | `pokpaymentgateway-prestashop-1.6.zip` |

## Install (1.7+)

Modules → Module Manager → Upload a module → drag in the zip → Configure.

## Install (1.6)

Back-office uploader (Modules and Services → Add a new module) or FTP (unzip into `/modules/`, then Install from the back office).

## Configure

Display name, Environment (Staging/Production), Key ID, Key Secret, Merchant ID, Order status on success. Save.

## Test

Add product to cart, checkout with POK Payments, test card `4242 4242 4242 4242`, confirm order status + POK SDK order ID visible in order details.

## Go live

- [ ] Replace staging credentials with production.
- [ ] Toggle Environment to Production.
- [ ] Real low-value test order, refund from POK dashboard.
- [ ] Confirm HTTPS.
- [ ] Test the 3-D Secure embedded-frame flow on a phone.

## Troubleshooting

| Symptom | Likely cause | Fix |
|---|---|---|
| POK doesn't appear at checkout | Currency/country restriction, or module disabled. | Check Restrictions tab, re-enable. |
| `401 Unauthorized` in logs | Credentials/environment mismatch. | Re-paste credentials, verify Staging vs Production. |
| Order stays `Awaiting payment` | Capture webhook failed to update order. | Confirm capture in POK dashboard, manually update order status. |
| Install fails "incompatible PrestaShop version" | Wrong archive for store version. | Download correct 1.6 vs 1.7 build. |

## Updating

Uninstall old (settings persist), install new zip, reconfirm credentials.

---

# REST API

> REST integration guide — staging quick start, authentication, SDK orders, and links to the full API reference at payments.doc.pokpay.io.

# REST API

The POK Payments REST API is the same surface every POK SDK is built on. Use it directly for languages with no official SDK, fine-grained control, or server-to-server integrations.

> [!note]
> **`keyId` and `keySecret` are issued by PokPay** from your merchant dashboard (E-payments dropdown → API Keys option — create one to generate keys). **Staging credentials** work only with `https://api-staging.pokpay.io`; **production credentials** only with `https://api.pokpay.io`. If you use a browser or mobile SDK, you still **create the SDK order with this API on the server** and pass the returned `id` to the client.

## Base URLs

| Environment | Base URL |
|---|---|
| Production | `https://api.pokpay.io` |
| Staging | `https://api-staging.pokpay.io` |

Start every new integration against **staging** — non-billing, safe for development. Test cards work against staging only.

## Authentication

`POST /auth/sdk/login` with `keyId`/`keySecret` → JSON body wraps `data.accessToken` (Bearer token) + expiry metadata. Re-login when expired.

```bash
curl -X POST https://api.pokpay.io/auth/sdk/login \
  -H 'Content-Type: application/json' \
  -d '{"keyId":"YOUR_KEY_ID","keySecret":"YOUR_KEY_SECRET"}'
```

```http
POST https://api.pokpay.io/auth/sdk/login
Content-Type: application/json

{
  "keyId": "YOUR_KEY_ID",
  "keySecret": "YOUR_KEY_SECRET"
}
---
{
  "statusCode": 200,
  "serverStatusCode":200,
  "data": {
    "accessToken": "eyJ...",
    "expiresIn": "600000",
    "tokenType": "bearer",
    "expiresAt": "....."
  },
  "message": "Success",
  "requestId": "......",
  "errors": []
}
```

> [!danger]
> Never embed `keyId` / `keySecret` in browser code.

### Test with Postman

1. **POST** `https://api.pokpay.io/auth/sdk/login`.
2. Body → raw → JSON: `{ "keyId": "...", "keySecret": "..." }`.
3. Copy `data.accessToken`.
4. On later requests: Authorization → Bearer Token.

---

## Quick start: prove your setup

Three-call path to verify credentials, routing, and `merchantId`:

1. **Login** — `POST /auth/sdk/login`. Store `data.accessToken`.
2. **Create an SDK order** — `POST /merchants/{merchantId}/sdk-orders`:

```http
POST https://api.pokpay.io/merchants/YOUR_MERCHANT_ID/sdk-orders
Content-Type: application/json
Authorization: Bearer eyJ...

{
  "amount": 100,
  "currencyCode": "EUR",
  "autoCapture": true,
  "shippingCost": 0,
  "webhookUrl": "{{webhookUrl}}",
  "redirectUrl": "{{redirectUrl}}",
  "deeplink": "{{deeplink}}"
}
---
{
  "statusCode": 200,
  "serverStatusCode": 99900201,
  "data": {
    "sdkOrder":{
       "id": "NEW_SDK_ORDER_ID",
       "amount": 100,
       "capturedAmount": 0,
       "currencyCode": "EUR"
    }
  }
}
```

`amount` is in **minor units** (e.g. `1000` = 10.00 for EUR).

3. **Fetch the order** — `GET /sdk-orders/{sdkOrderId}` with the same bearer token.

For browser/mobile checkouts, pass `data.id` (well: `data.sdkOrder.id`) from step 2 into the client SDK as `orderId`. After the customer completes the flow, complete payment server-side with `guest-confirm`, `confirm`, or `capture` depending on your integration.

Full request/response schemas: https://payments.doc.pokpay.io/

---

## Full API reference

**https://payments.doc.pokpay.io/** — the authoritative spec for every URL, request schema, response shape, and error code.

## Most-used endpoints

| Method | Path | Purpose |
| ------ | ---- | ------- |
| `POST` | `/auth/sdk/login` | Exchange `keyId` / `keySecret` for an access token. |
| `POST` | `/merchants/{merchantId}/sdk-orders` | Create an SDK order. The returned `id` is what your frontend uses. |
| `POST` | `/merchants/{merchantId}/sdk-orders/{sdkOrderId}/capture` | Capture an authorized order (server-side checkout flow). |
| `POST` | `/sdk-orders/{sdkOrderId}/confirm` | Confirm an authenticated order. |
| `POST` | `/sdk-orders/{sdkOrderId}/guest-confirm` | Confirm a guest-checkout order. |
| `GET` | `/sdk-orders/{sdkOrderId}` | Fetch the current state of an order. |
| `POST` | `/credit-debit-cards/{cardId}/setup-tokenized-3ds` | Set up 3-D Secure for a saved card before charging it via the JS library. |

## Which endpoint finishes checkout?

| Scenario | Typical server call |
| -------- | -------------------- |
| Guest checkout — customer not logged into your system | `POST /sdk-orders/{sdkOrderId}/guest-confirm` |
| Authenticated customer on your system | `POST /sdk-orders/{sdkOrderId}/confirm` |
| Server-led capture flow | `POST /merchants/{merchantId}/sdk-orders/{sdkOrderId}/capture` |

## Troubleshooting

| Symptom | Likely cause | What to try |
| ------- | ------------- | ------------ |
| `401` on protected routes | Wrong environment, invalid credentials, or expired token | Match host to Base URLs; re-login for fresh token |
| `403` on `POST .../sdk-orders` | `merchantId` in the URL doesn't belong to the merchant tied to your `keyId`/`keySecret` | Use the correct merchant id for that credential pair |

## Standard error shape

```json
{
  "statusCode": 401,
  "message": "Invalid or expired access token",
  "error": "Unauthorized"
}
```

Surface `message` to logs only — never to end users; show a friendly retry prompt instead.
