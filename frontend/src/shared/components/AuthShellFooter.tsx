import type { ReactNode } from "react";

interface AuthShellFooterProps {
  children: ReactNode;
  secondary?: ReactNode;
}

export function AuthShellFooter({ children, secondary }: AuthShellFooterProps) {
  return (
    <div className="mt-4 pt-4 border-t border-border/50 text-center text-sm text-muted-foreground space-y-1.5">
      {children}
      {secondary && <div className="text-xs">{secondary}</div>}
    </div>
  );
}
