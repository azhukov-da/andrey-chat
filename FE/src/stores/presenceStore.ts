import { create } from 'zustand'
import type { PresenceStatus } from '@/types'

interface PresenceState {
  statuses: Map<string, PresenceStatus>
  update: (userId: string, status: PresenceStatus) => void
  get: (userId: string) => PresenceStatus
}

export const usePresenceStore = create<PresenceState>()((set, get) => ({
  statuses: new Map(),

  update(userId, status) {
    set((s) => {
      const next = new Map(s.statuses)
      next.set(userId, status)
      return { statuses: next }
    })
  },

  get(userId) {
    return get().statuses.get(userId) ?? 'offline'
  },
}))
