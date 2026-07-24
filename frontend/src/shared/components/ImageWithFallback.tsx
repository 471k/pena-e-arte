import { useState, type ImgHTMLAttributes } from "react";
import { ImageOff } from "lucide-react";
import { cn } from "@/shared/utils/cn";

interface ImageWithFallbackProps extends ImgHTMLAttributes<HTMLImageElement> {
  /** Applied to the placeholder shown in place of a broken image. Falls back to className. */
  fallbackClassName?: string;
}

/**
 * <img> that swaps to a muted placeholder icon on load failure instead of the
 * browser's broken-image glyph — for any photo sourced from a URL that can
 * legitimately go stale (deleted storage object, CDN hiccup).
 */
export function ImageWithFallback({
  fallbackClassName, className, alt, onError, ...props
}: ImageWithFallbackProps) {
  const [failed, setFailed] = useState(false);

  if (failed) {
    return (
      <div
        role="img"
        aria-label={alt || "Image unavailable"}
        className={cn(
          "flex items-center justify-center bg-muted text-muted-foreground/60",
          fallbackClassName ?? className,
        )}
      >
        <ImageOff className="h-6 w-6" />
      </div>
    );
  }

  return (
    <img
      {...props}
      alt={alt}
      className={className}
      onError={(e) => {
        setFailed(true);
        onError?.(e);
      }}
    />
  );
}
