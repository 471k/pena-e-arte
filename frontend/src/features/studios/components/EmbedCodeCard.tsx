import { useState } from "react";
import { Check, Code2, Copy } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { useGetMyStudioQuery } from "../studiosApi";

export function EmbedCodeCard() {
  const { data: studio } = useGetMyStudioQuery();
  const [copied, setCopied] = useState(false);

  if (!studio) return null;

  const EMBED_BASE = import.meta.env.VITE_PUBLIC_URL ?? window.location.origin;
  const embedUrl   = `${EMBED_BASE}/embed/${studio.slug}`;
  const iframeCode = [
    `<!-- Adjust width/height to fit your layout -->`,
    `<iframe`,
    `  src="${embedUrl}"`,
    `  width="380"`,
    `  height="600"`,
    `  frameborder="0"`,
    `  title="Book at ${studio.name}"`,
    `  allow="payment"`,
    `></iframe>`,
  ].join("\n");

  async function handleCopy() {
    await navigator.clipboard.writeText(iframeCode);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base flex items-center gap-2">
          <Code2 className="h-4 w-4" />
          Booking widget
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <p className="text-sm text-muted-foreground">
          Paste this snippet into any webpage to embed your booking widget.
        </p>
        <div className="relative">
          <pre className="bg-muted rounded-md px-4 py-3 text-xs font-mono overflow-x-auto whitespace-pre">
            {iframeCode}
          </pre>
          <Button
            size="icon"
            variant="ghost"
            className="absolute top-2 right-2 h-7 w-7"
            onClick={handleCopy}
            aria-label="Copy embed code"
          >
            {copied ? <Check className="h-3.5 w-3.5 text-green-500" /> : <Copy className="h-3.5 w-3.5" />}
          </Button>
        </div>
        <div className="flex items-center gap-2">
          <p className="text-xs text-muted-foreground">
            Preview your booking widget in a new tab.
          </p>
          <Button
            variant="link"
            size="sm"
            className="h-auto p-0 text-xs"
            asChild
          >
            <a href={embedUrl} target="_blank" rel="noopener noreferrer">
              Open preview →
            </a>
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
