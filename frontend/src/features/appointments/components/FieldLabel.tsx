import { Label } from "@/shared/components/ui/label";

export function FieldLabel({
  htmlFor,
  required = false,
  children,
}: {
  htmlFor:   string;
  required?: boolean;
  children:  React.ReactNode;
}) {
  return (
    <Label htmlFor={htmlFor} className="text-xs font-medium text-muted-foreground">
      {children}
      {required && (
        <span aria-hidden="true" className="ml-0.5 text-destructive">*</span>
      )}
    </Label>
  );
}
