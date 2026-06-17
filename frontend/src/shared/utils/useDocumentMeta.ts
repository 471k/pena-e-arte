import { useEffect } from "react";

export interface DocMeta {
  title: string;
  description?: string;
  ogImage?: string;
  canonical: string;
}

export function useDocumentMeta({ title, description, ogImage, canonical }: DocMeta): void {
  useEffect(() => {
    const previousTitle = document.title;
    const injected: Element[] = [];

    function injectMeta(attrs: Record<string, string>): void {
      const el = document.createElement("meta");
      Object.entries(attrs).forEach(([k, v]) => el.setAttribute(k, v));
      document.head.appendChild(el);
      injected.push(el);
    }

    document.title = title;
    injectMeta({ property: "og:title", content: title });

    if (description) {
      injectMeta({ name: "description", content: description });
      injectMeta({ property: "og:description", content: description });
    }

    if (ogImage) {
      injectMeta({ property: "og:image", content: ogImage });
    }

    const link = document.createElement("link");
    link.setAttribute("rel", "canonical");
    link.setAttribute("href", canonical);
    document.head.appendChild(link);
    injected.push(link);

    return () => {
      document.title = previousTitle;
      injected.forEach((el) => el.remove());
    };
  }, [title, description, ogImage, canonical]);
}
