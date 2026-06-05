import { useNavigate } from "react-router-dom";
import { RefreshCw } from "lucide-react";
import { Button } from "@/shared/components/ui/button";

export function ConnectRefreshPage() {
  const navigate = useNavigate();

  return (
    <div className="min-h-screen bg-background flex items-center justify-center">
      <div className="text-center space-y-4 px-6 max-w-sm">
        <RefreshCw className="h-10 w-10 text-muted-foreground mx-auto" />
        <p className="text-base font-semibold">Onboarding link expired</p>
        <p className="text-sm text-muted-foreground">
          The Stripe onboarding link has expired. Please start the process again to get a new link.
        </p>
        <Button onClick={() => navigate("/studio/connect")}>Restart onboarding</Button>
      </div>
    </div>
  );
}
