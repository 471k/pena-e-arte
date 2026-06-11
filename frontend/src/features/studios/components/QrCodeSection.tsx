import { Download, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { useGetMyStudioQuery, useGetStudioQrCodeQuery } from "../studiosApi";

export function QrCodeSection() {
  const { data: studio } = useGetMyStudioQuery();
  const { data: blobUrl, isLoading, isError } = useGetStudioQrCodeQuery(studio?.id ?? "", {
    skip: !studio?.id,
  });

  if (!studio) return null;

  function handleDownload() {
    if (!blobUrl) return;
    const anchor = document.createElement("a");
    anchor.href  = blobUrl;
    anchor.download = `${studio!.slug}-qr.png`;
    anchor.click();
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle className="text-base">Marketing QR code</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-xs text-muted-foreground">
          Scan to book — add this to your window, business cards, or social bio.
        </p>

        <div className="flex flex-col items-start gap-4">
          {isLoading && (
            <div className="flex h-48 w-48 items-center justify-center rounded-md border bg-muted">
              <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
            </div>
          )}

          {isError && (
            <p className="text-sm text-destructive">Failed to load QR code.</p>
          )}

          {blobUrl && !isLoading && (
            <img
              src={blobUrl}
              alt={`QR code for ${studio.name}`}
              className="h-48 w-48 rounded-md border object-contain"
            />
          )}

          <Button
            variant="outline"
            size="sm"
            onClick={handleDownload}
            disabled={!blobUrl || isLoading}
            className="gap-2"
          >
            <Download className="h-4 w-4" />
            Download PNG
          </Button>
        </div>
      </CardContent>
    </Card>
  );
}
