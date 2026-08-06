// crypto.randomUUID() only exists in secure contexts (HTTPS or localhost) — it's
// undefined when the app is reached over plain HTTP on a non-localhost origin (e.g.
// a LAN IP during device testing). These IDs are client-side correlation IDs only
// (visitor/upload session tracking), never used for security, so a Math.random-based
// fallback is an acceptable trade-off for uptime over cryptographic strength.
export function generateUuid(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    const v = c === "x" ? r : (r & 0x3) | 0x8;
    return v.toString(16);
  });
}
