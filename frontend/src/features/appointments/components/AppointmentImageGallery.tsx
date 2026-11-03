/** A labelled grid of an appointment's images, each opening full size in a new tab. */
export function AppointmentImageGallery({ label, imageAlt, urls }: { label: string; imageAlt: string; urls: string[] }) {
  if (urls.length === 0) return null;
  return (
    <div className="py-2 space-y-1.5">
      <span className="text-sm text-muted-foreground">{label}</span>
      <div className="grid grid-cols-4 gap-2">
        {urls.map((url) => (
          <a
            key={url}
            href={url}
            target="_blank"
            rel="noreferrer"
            className="block aspect-square rounded-md overflow-hidden
                       border border-border/40 bg-muted/30"
          >
            <img src={url} alt={imageAlt} className="h-full w-full object-cover" />
          </a>
        ))}
      </div>
    </div>
  );
}
