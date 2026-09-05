import { useEffect } from "react";
import { Download, Loader2 } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import { Card, CardContent, CardHeader, CardTitle } from "@/shared/components/ui/card";
import { useGetMyStudioQuery, useGetStudioQrCodeQuery, useLazyGetStudioQrCodeQuery } from "../studiosApi";

export function QrCodeSection() {
  const { data: studio } = useGetMyStudioQuery();
  const { data: blobUrl, isLoading, isError } = useGetStudioQrCodeQuery(
    { id: studio?.id ?? "", format: "png" },
    { skip: !studio?.id },
  );
  const [fetchSvg, { isFetching: isFetchingSvg }] = useLazyGetStudioQrCodeQuery();

  useEffect(() => {
    return () => {
      if (blobUrl) URL.revokeObjectURL(blobUrl);
    };
  }, [blobUrl]);

  if (!studio) return null;

  function handleDownload() {
    if (!blobUrl) return;
    const anchor = document.createElement("a");
    anchor.href  = blobUrl;
    anchor.download = `${studio!.slug}-qr.png`;
    anchor.click();
  }

  async function handleDownloadSvg() {
    const result = await fetchSvg({ id: studio!.id, format: "svg" });
    if (!result.data) return;
    const anchor = document.createElement("a");
    anchor.href = result.data;
    anchor.download = `${studio!.slug}-qr.svg`;
    anchor.click();
    URL.revokeObjectURL(result.data);
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

        <a
          href={`https://tattooos.co/s/${studio.slug}`}
          target="_blank"
          rel="noopener noreferrer"
          className="text-xs font-mono text-muted-foreground hover:text-foreground underline underline-offset-2 break-all"
        >
          tattooos.co/s/{studio.slug}
        </a>

        <div className="flex flex-col items-center gap-4">
          {isLoading && (
            <div data-testid="qr-loading" className="flex h-48 w-48 items-center justify-center rounded-md border bg-muted">
              <Loader2 className="h-6 w-6 animate-spin text-muted-foreground" />
            </div>
          )}

          {isError && (
            <p className="text-sm text-destructive-text">Failed to load QR code.</p>
          )}

          {blobUrl && !isLoading && (
            <img
              data-testid="qr-image"
              src={blobUrl}
              alt={`QR code for ${studio.name}`}
              className="h-48 w-48 rounded-md border object-contain"
            />
          )}

          <div className="flex gap-2">
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
            <Button
              variant="outline"
              size="sm"
              onClick={handleDownloadSvg}
              disabled={isFetchingSvg}
              className="gap-2"
            >
              {isFetchingSvg ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <Download className="h-4 w-4" />
              )}
              Download SVG
            </Button>
          </div>
        </div>
      </CardContent>
    </Card>
  );
}
