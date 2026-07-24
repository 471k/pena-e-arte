import type { ReactNode } from "react";
import { cn } from "@/shared/utils/cn";

interface FieldHintProps {
  children: ReactNode;
  className?: string;
  id?: string;
}

export function FieldHint({ children, className, id }: FieldHintProps) {
  return (
    <p id={id} className={cn("text-xs text-muted-foreground", className)}>
      {children}
    </p>
  );
}
