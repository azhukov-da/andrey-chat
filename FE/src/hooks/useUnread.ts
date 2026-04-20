import { useUnreadStore } from '@/stores/unreadStore'
import { markRead as apiMarkRead } from '@/api/messages'
import { getHubConnection } from '@/realtime/hubClient'

export function useUnread(roomId?: string) {
  const count = useUnreadStore((s) => (roomId ? (s.counts.get(roomId) ?? 0) : 0))
  const total = useUnreadStore((s) => s.total())

  const markRead = async (msgId: string) => {
    if (!roomId) return
    useUnreadStore.getState().clear(roomId)
    try {
      const hub = getHubConnection()
      await hub.invoke('MarkRead', roomId, msgId)
    } catch {
      await apiMarkRead(roomId, msgId)
    }
  }

  return { count, total, markRead }
}
