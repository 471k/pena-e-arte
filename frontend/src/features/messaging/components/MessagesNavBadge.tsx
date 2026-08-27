import { Link } from "react-router-dom";
import { MessageCircle } from "lucide-react";
import { useGetUnreadCountQuery } from "../messagingApi";

// Mirrors NotificationBell's badge shape/placement in each layout header.
export function MessagesNavBadge() {
  const { data: unreadCount } = useGetUnreadCountQuery();
  const count = unreadCount ?? 0;

  return (
    <Link
      to="/messages"
      aria-label={count > 0 ? `View messages, ${count} unread` : "View messages"}
      title="Messages"
      data-tour="messages-nav"
      className="relative h-8 w-8 flex items-center justify-center rounded-md text-muted-foreground hover:text-foreground hover:bg-muted transition-colors"
    >
      <MessageCircle className="h-4 w-4" />
      {count > 0 && (
        <span className="absolute -top-1 -right-1 flex h-4 min-w-4 items-center justify-center rounded-full bg-destructive px-1 text-[10px] font-semibold leading-none text-destructive-foreground">
          {count > 9 ? "9+" : count}
        </span>
      )}
    </Link>
  );
}
