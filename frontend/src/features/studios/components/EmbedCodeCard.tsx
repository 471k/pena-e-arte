import { useState } from "react";
import { Check, Code2, Copy } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { useGetMyStudioQuery } from "../studiosApi";

export function EmbedCodeCard() {
  const { data: studio } = useGetMyStudioQuery();
  const [copied, setCopied] = useState(false);

  if (!studio) return null;

  const embedUrl  = `${window.location.origin}/embed/${studio.slug}`;
  const iframeCode = `<iframe\n  src="${embedUrl}"\n  width="380"\n  height="600"\n  frameborder="0"\n  title="Book at ${studio.name}"\n  allow="payment"\n></iframe>`;

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
        <p className="text-xs text-muted-foreground">
          Preview:{" "}
          <a
            href={embedUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="underline hover:text-foreground"
          >
            {embedUrl}
          </a>
        </p>
      </CardContent>
    </Card>
  );
}
