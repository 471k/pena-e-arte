import { useNavigate } from "react-router-dom";
import { CheckCircle } from "lucide-react";
import { Button } from "@/shared/components/ui/button";

export function ConnectReturnPage() {
  const navigate = useNavigate();

  return (
    <div className="min-h-screen bg-background flex items-center justify-center">
      <div className="text-center space-y-4 px-6 max-w-sm">
        <CheckCircle className="h-12 w-12 text-green-500 mx-auto" />
        <p className="text-base font-semibold">Stripe onboarding complete</p>
        <p className="text-sm text-muted-foreground">
          Your studio has been connected to Stripe. You can now accept deposit payments from clients.
        </p>
        <Button onClick={() => navigate("/billing")}>Back to Billing</Button>
      </div>
    </div>
  );
}
