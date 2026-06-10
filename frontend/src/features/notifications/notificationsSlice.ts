import { createSlice, type PayloadAction } from "@reduxjs/toolkit";

interface NotificationsState {
  unreadCount: number;
  isInboxOpen: boolean;
}

const initialState: NotificationsState = {
  unreadCount: 0,
  isInboxOpen: false,
};

const notificationsSlice = createSlice({
  name: "notifications",
  initialState,
  reducers: {
    incrementUnread: (state) => {
      state.unreadCount += 1;
    },
    clearUnread: (state) => {
      state.unreadCount = 0;
    },
    setUnreadCount: (state, { payload }: PayloadAction<number>) => {
      state.unreadCount = payload;
    },
    toggleInbox: (state) => {
      state.isInboxOpen = !state.isInboxOpen;
    },
  },
});

export const { incrementUnread, clearUnread, setUnreadCount, toggleInbox } =
  notificationsSlice.actions;
export default notificationsSlice.reducer;
