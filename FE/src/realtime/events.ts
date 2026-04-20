import type { QueryClient } from '@tanstack/react-query'
import type { Message } from '@/types'
import { usePresenceStore } from '@/stores/presenceStore'
import { useUnreadStore } from '@/stores/unreadStore'
import { useUiStore } from '@/stores/uiStore'
import type { HubConnection } from '@microsoft/signalr'

interface MessageEditedPayload {
  messageId: string
  newText: string
  editedAt: string
}

interface MessageDeletedPayload {
  messageId: string
}

interface PresenceChangedPayload {
  userId: string
  status: string
}

interface RoomMembershipChangedPayload {
  roomId: string
  userId: string
  action: string
}

interface UnreadUpdatedPayload {
  roomId: string
  unreadCount: number
}

interface RoomDeletedPayload {
  roomId: string
}

type CursorPaged<T> = { items: T[]; nextCursor?: string | null }
type Pages<T> = { pages: CursorPaged<T>[] }

export function registerHubEvents(hub: HubConnection, queryClient: QueryClient, navigate: (path: string) => void) {
  hub.on('MessageReceived', (message: Message) => {
    const roomId = message.roomId
    queryClient.setQueryData<Pages<Message>>(['messages', roomId], (old) => {
      if (!old) return old
      const [first, ...rest] = old.pages
      if (!first) return old
      return { ...old, pages: [{ ...first, items: [message, ...first.items] }, ...rest] }
    })

    const activeRoom = useUiStore.getState().activeRoomId
    if (activeRoom !== roomId) {
      useUnreadStore.getState().increment(roomId)
    }
  })

  hub.on('MessageEdited', (payload: MessageEditedPayload) => {
    queryClient.setQueriesData<Pages<Message>>({ queryKey: ['messages'] }, (old) => {
      if (!old) return old
      return {
        ...old,
        pages: old.pages.map((page) => ({
          ...page,
          items: page.items.map((m) =>
            m.id === payload.messageId
              ? { ...m, text: payload.newText, editedAt: payload.editedAt }
              : m
          ),
        })),
      }
    })
  })

  hub.on('MessageDeleted', (payload: MessageDeletedPayload) => {
    queryClient.setQueriesData<Pages<Message>>({ queryKey: ['messages'] }, (old) => {
      if (!old) return old
      return {
        ...old,
        pages: old.pages.map((page) => ({
          ...page,
          items: page.items.map((m) =>
            m.id === payload.messageId ? { ...m, isDeleted: true, text: '' } : m
          ),
        })),
      }
    })
  })

  hub.on('PresenceChanged', (payload: PresenceChangedPayload) => {
    const status = payload.status.toLowerCase() as 'online' | 'afk' | 'offline'
    usePresenceStore.getState().update(payload.userId, status)
  })

  hub.on('RoomMembershipChanged', (payload: RoomMembershipChangedPayload) => {
    void queryClient.invalidateQueries({ queryKey: ['rooms', 'mine'] })
    void queryClient.invalidateQueries({ queryKey: ['rooms', payload.roomId, 'members'] })
  })

  hub.on('RoomDeleted', (payload: RoomDeletedPayload) => {
    void queryClient.invalidateQueries({ queryKey: ['rooms'] })
    const activeRoom = useUiStore.getState().activeRoomId
    if (activeRoom === payload.roomId) {
      navigate('/rooms')
    }
  })

  hub.on('FriendRequestReceived', () => {
    void queryClient.invalidateQueries({ queryKey: ['friends'] })
  })

  hub.on('UnreadUpdated', (payload: UnreadUpdatedPayload) => {
    useUnreadStore.getState().set(payload.roomId, payload.unreadCount)
  })
}
