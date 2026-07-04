import { useCallback, useState } from "react";
import { useAppDispatch, useAppSelector } from "@/app/hooks";
import { decodeToken } from "@/shared/utils/jwt";
import { useSwitchStudioMutation } from "./authApi";
import { setCredentials } from "./authSlice";

interface UseEnsureActiveStudioResult {
  isSwitching:     boolean;
  isNewMembership: boolean;
  error:           string | null;
  /** No-ops if targetStudioId is already the active studio. Returns true on success. */
  ensure: (targetStudioId: string | null | undefined) => Promise<boolean>;
}

export function useEnsureActiveStudio(): UseEnsureActiveStudioResult {
  const dispatch                      = useAppDispatch();
  const currentTenantId               = useAppSelector((s) => s.auth.tenantId);
  const [switchStudio, { isLoading }] = useSwitchStudioMutation();
  const [isNewMembership, setIsNewMembership] = useState(false);
  const [error, setError]             = useState<string | null>(null);

  const ensure = useCallback(
    async (targetStudioId: string | null | undefined): Promise<boolean> => {
      if (!targetStudioId || targetStudioId === currentTenantId) return true;

      setError(null);
      try {
        const response = await switchStudio({ studioId: targetStudioId }).unwrap();
        const decoded  = decodeToken(response.accessToken);
        dispatch(setCredentials({ ...decoded, refreshToken: response.refreshToken }));
        setIsNewMembership(response.isNewMembership);
        return true;
      } catch {
        setError("Couldn't switch studios. Please try again.");
        return false;
      }
    },
    [currentTenantId, switchStudio, dispatch],
  );

  return { isSwitching: isLoading, isNewMembership, error, ensure };
}
