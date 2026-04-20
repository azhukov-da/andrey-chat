import { usePresenceStore } from '@/stores/presenceStore'
import type { PresenceStatus } from '@/types'

export function usePresence(userId: string | undefined): PresenceStatus {
  return usePresenceStore((s) => (userId ? (s.statuses.get(userId) ?? 'offline') : 'offline'))
}
