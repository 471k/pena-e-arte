import { useState, type ReactNode } from "react";
import { Link } from "react-router-dom";
import { HelpCircle, MessageCircle, MessageSquareMore, MoreHorizontal } from "lucide-react";
import { Button } from "@/shared/components/ui/button";
import {
  DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger,
} from "@/shared/components/ui/dropdown-menu";
import { NotificationBell } from "@/features/notifications/components/NotificationBell";
import { HelpMenu, type HelpMenuTourProps } from "@/features/help/components/HelpMenu";
import { MessagesNavBadge } from "@/features/messaging/components/MessagesNavBadge";
import { useGetUnreadCountQuery } from "@/features/messaging/messagingApi";
import { HEADER_MORE_TOUR_ID } from "@/features/help/tours/headerFold";

interface HeaderActionsProps {
  /** From useHeaderFold on the layout's header: the one value that decides both this and the tour. */
  folded: boolean;
  /** Owner and artist: opens the layout's feedback dialog. */
  onFeedback?: () => void;
  /** Owner, artist and client. */
  showMessages?: boolean;
  /** The layout's tour wiring, passed through to HelpMenu. */
  help: HelpMenuTourProps;
  /** Stays outside the fold, before the notification bell (the owner's studio-invite bell, which renders only
   * while invites are pending and carries that count). */
  pinned?: ReactNode;
  userMenu: ReactNode;
}

const BADGE = "absolute -top-1 -right-1 flex h-4 min-w-4 items-center justify-center rounded-full bg-destructive px-1 "
  + "text-[10px] font-semibold leading-none text-destructive-foreground";

/**
 * The header's right-hand controls (S05). Inline when there is room. When the header is too narrow for them
 * (useHeaderFold, measured in rem so 200% text folds), feedback, help and messages go into one "More" menu, whose
 * trigger carries the unread-message count. The notification bell and the account menu never fold: they are what
 * people reach for under pressure, and the bell carries unread state (Phi, 23 Nov).
 *
 * HelpMenu stays mounted either way: it owns the Help sheet and runs the tours, so it can't live inside a menu
 * that unmounts when it closes. Folded, it hides its own trigger and "Help" in the menu opens it.
 */
export function HeaderActions(props: HeaderActionsProps) {
  // The unread query mounts only where messages exist: an RTK Query hook throws when its API isn't in the store,
  // even skipped, and the admin's store has no messaging API.
  return props.showMessages
    ? <WithUnreadCount>{(unread) => <Actions {...props} unread={unread} />}</WithUnreadCount>
    : <Actions {...props} unread={0} />;
}

function WithUnreadCount({ children }: { children: (unread: number) => ReactNode }) {
  const { data } = useGetUnreadCountQuery();
  return <>{children(data ?? 0)}</>;
}

function Actions({
  folded, onFeedback, showMessages = false, help, pinned, userMenu, unread,
}: HeaderActionsProps & { unread: number }) {
  const [helpOpen, setHelpOpen] = useState(false);

  // A menu holding only Help would be one control swapped for another: fold only when it saves something.
  const foldableCount = 1 + (onFeedback ? 1 : 0) + (showMessages ? 1 : 0);
  const useMore = folded && foldableCount > 1;

  const moreLabel = unread > 0 ? `More: help, messages and more, ${unread} unread messages` : "More: help, messages and more";

  return (
    <div className="ml-auto flex items-center gap-1 sm:gap-3">
      {!useMore && onFeedback && (
        <Button
          variant="ghost" size="icon" className="h-8 w-8"
          onClick={onFeedback} title="Send feedback" aria-label="Send feedback"
        >
          <MessageSquareMore className="h-4 w-4" />
        </Button>
      )}
      <HelpMenu {...help} open={helpOpen} onOpenChange={setHelpOpen} showTrigger={!useMore} headerFolded={useMore} />
      {!useMore && showMessages && <MessagesNavBadge />}
      {useMore && (
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button
              variant="ghost" size="icon" className="relative h-8 w-8"
              aria-label={moreLabel} title="More" data-tour={HEADER_MORE_TOUR_ID}
            >
              <MoreHorizontal className="h-4 w-4" />
              {unread > 0 && <span className={BADGE} aria-hidden="true">{unread > 9 ? "9+" : unread}</span>}
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end" className="min-w-48">
            <DropdownMenuItem onSelect={() => setHelpOpen(true)}>
              <HelpCircle className="h-4 w-4" aria-hidden="true" />
              Help
            </DropdownMenuItem>
            {showMessages && (
              <DropdownMenuItem asChild>
                <Link to="/messages" aria-label={unread > 0 ? `Messages, ${unread} unread` : "Messages"}>
                  <MessageCircle className="h-4 w-4" aria-hidden="true" />
                  <span className="flex-1">Messages</span>
                  {unread > 0 && (
                    <span className="rounded-full bg-destructive px-1.5 text-[10px] font-semibold leading-4 text-destructive-foreground" aria-hidden="true">
                      {unread > 9 ? "9+" : unread}
                    </span>
                  )}
                </Link>
              </DropdownMenuItem>
            )}
            {onFeedback && (
              <DropdownMenuItem onSelect={onFeedback}>
                <MessageSquareMore className="h-4 w-4" aria-hidden="true" />
                Send feedback
              </DropdownMenuItem>
            )}
          </DropdownMenuContent>
        </DropdownMenu>
      )}
      {pinned}
      <NotificationBell />
      {userMenu}
    </div>
  );
}
