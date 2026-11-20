import { useContext, useEffect } from "react";
import { UNSAFE_DataRouterContext, useBlocker } from "react-router-dom";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription,
  AlertDialogFooter, AlertDialogHeader, AlertDialogTitle,
} from "@/shared/components/ui/alert-dialog";

/**
 * S04 (slice 7 phase B): leaving a form with entered details is a deliberate choice, not an accident. A route
 * change inside the app asks "Stay on this page" / "Leave and lose it"; closing or reloading the tab gets the
 * browser's own prompt. This is the recovery path that needs no storage: what was entered is never saved
 * anywhere (durable drafts are phase D, not built, because they would hold health answers and photographs).
 */
export function UnsavedChangesGuard({ when }: { when: boolean }) {
  useEffect(() => {
    if (!when) return;
    function onBeforeUnload(e: BeforeUnloadEvent) {
      e.preventDefault();
      e.returnValue = "";
    }
    window.addEventListener("beforeunload", onBeforeUnload);
    return () => window.removeEventListener("beforeunload", onBeforeUnload);
  }, [when]);

  // useBlocker needs a data router (the app has one). Under a plain router, as in some component tests, only the
  // browser prompt above applies.
  const dataRouter = useContext(UNSAFE_DataRouterContext);
  return dataRouter ? <LeaveDialog when={when} /> : null;
}

function LeaveDialog({ when }: { when: boolean }) {
  const blocker = useBlocker(({ currentLocation, nextLocation }) =>
    when && currentLocation.pathname !== nextLocation.pathname);

  if (blocker.state !== "blocked") return null;
  return (
    <AlertDialog open onOpenChange={(open) => { if (!open) blocker.reset(); }}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>Leave this page?</AlertDialogTitle>
          <AlertDialogDescription>
            What you&apos;ve entered isn&apos;t saved anywhere, so it will be lost.
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel onClick={() => blocker.reset()}>Stay on this page</AlertDialogCancel>
          <AlertDialogAction onClick={() => blocker.proceed()}>Leave and lose it</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
