import { useEffect } from "react";

export interface DocMeta {
  title:        string;
  description?: string;
  ogImage?:     string;
  canonical:    string;
}

const ATTR = "data-doc-meta";

export function useDocumentMeta({ title, description, ogImage, canonical }: DocMeta): void {
  useEffect(() => {
    const prevTitle = document.title;

    // Remove any tags this hook injected on the previous render (handles StrictMode
    // double-fire and hot reloads without leaving duplicate tags in <head>).
    document.querySelectorAll(`[${ATTR}]`).forEach((el) => el.remove());

    function inject(tag: "meta" | "link", attrs: Record<string, string>): void {
      const el = document.createElement(tag);
      Object.entries(attrs).forEach(([k, v]) => el.setAttribute(k, v));
      el.setAttribute(ATTR, "1");
      document.head.appendChild(el);
    }

    document.title = title;
    inject("meta", { property: "og:title",   content: title });
    inject("meta", { property: "og:type",    content: "website" });
    inject("meta", { property: "og:url",     content: canonical });
    inject("meta", { name: "twitter:card",   content: "summary_large_image" });
    inject("meta", { name: "twitter:title",  content: title });

    if (description) {
      inject("meta", { name: "description",             content: description });
      inject("meta", { property: "og:description",      content: description });
      inject("meta", { name: "twitter:description",     content: description });
    }

    if (ogImage) {
      inject("meta", { property: "og:image",  content: ogImage });
      inject("meta", { name: "twitter:image", content: ogImage });
    }

    inject("link", { rel: "canonical", href: canonical });

    return () => {
      document.title = prevTitle;
      document.querySelectorAll(`[${ATTR}]`).forEach((el) => el.remove());
    };
  }, [title, description, ogImage, canonical]);
}
