import type { ReactNode } from "react";
import { PublicPageHeader } from "./PublicPageHeader";
import { SiteFooter } from "@/shared/components/SiteFooter";
import { useDocumentMeta } from "@/shared/utils/useDocumentMeta";

interface PublicContentLayoutProps {
  title: string;
  description?: string;
  /** Path fragment used for the canonical URL, e.g. "/privacy". */
  canonicalPath: string;
  children: ReactNode;
}

// Shared shell for the public, unauthenticated content surfaces (Home + the four
// policy pages). Wraps them in the same PublicPageHeader used by the portfolio
// pages and the site-wide legal SiteFooter, and sets per-page document metadata.
export function PublicContentLayout({
  title,
  description,
  canonicalPath,
  children,
}: PublicContentLayoutProps) {
  useDocumentMeta({
    title,
    description,
    canonical: `${window.location.origin}${canonicalPath}`,
  });

  return (
    <div className="flex min-h-screen flex-col bg-background">
      <PublicPageHeader />
      <main className="mx-auto w-full max-w-3xl flex-1 px-4 py-10">{children}</main>
      <SiteFooter />
    </div>
  );
}
