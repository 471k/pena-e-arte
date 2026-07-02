import { useEffect } from "react";

const ATTR = "data-structured-data";

/** Injects a JSON-LD <script> tag into <head> for rich search-result snippets. */
export function useStructuredData(schema: Record<string, unknown> | null): void {
  useEffect(() => {
    if (!schema) return;

    const script = document.createElement("script");
    script.type = "application/ld+json";
    script.setAttribute(ATTR, "1");
    script.text = JSON.stringify(schema);
    document.head.appendChild(script);

    return () => { script.remove(); };
  }, [schema]);
}
