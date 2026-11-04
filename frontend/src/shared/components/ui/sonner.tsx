import { Toaster as Sonner } from "sonner";

type ToasterProps = React.ComponentProps<typeof Sonner>;

// Toast colours come from Sonner's own custom properties (--normal-bg, --normal-text, ...),
// set to app tokens in index.css. Tailwind classes cannot style toasts: Sonner's stylesheet is
// unlayered, and unlayered CSS beats every Tailwind v4 utility layer regardless of specificity.
// The `toastOptions.classNames` that used to sit here (bg/text/border/shadow on the toast,
// description, action and cancel buttons) never painted anything for that reason, so they
// were removed on 2026-11-04 with no visual change.
export function Toaster({ ...props }: ToasterProps) {
  return <Sonner theme="system" {...props} />;
}
