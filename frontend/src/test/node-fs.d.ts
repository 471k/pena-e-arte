// Tests run in Node, but src is type-checked as browser code (tsconfig.app.json has no Node types). This declares
// the one Node API a test uses (ActionReceipt.test.tsx reads AuditActions.cs) instead of adding Node types to the
// whole app. App code must not import it: there is no node:fs in a browser.
declare module "node:fs" {
  export function readFileSync(path: string, encoding: "utf8"): string;
}
