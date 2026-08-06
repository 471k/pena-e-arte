// crypto.randomUUID() only exists in secure contexts (HTTPS or localhost) — it's
// undefined when the app is reached over plain HTTP on a non-localhost origin (e.g.
// a LAN IP during device testing). crypto.getRandomValues(), unlike randomUUID(), is
// NOT restricted to secure contexts, so the fallback builds a v4 UUID from it directly
// rather than falling back further to Math.random() (flagged by CodeQL as insecure
// randomness — correctly, since these values become part of R2 object storage keys).
export function generateUuid(): string {
  if (typeof crypto !== "undefined" && typeof crypto.randomUUID === "function") {
    return crypto.randomUUID();
  }
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  bytes[6] = (bytes[6] & 0x0f) | 0x40; // version 4
  bytes[8] = (bytes[8] & 0x3f) | 0x80; // variant 10
  const hex = Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
