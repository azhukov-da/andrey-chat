import { create } from 'zustand'

interface UnreadState {
  counts: Map<string, number>
  set: (roomId: string, count: number) => void
  increment: (roomId: string) => void
  clear: (roomId: string) => void
  total: () => number
}

export const useUnreadStore = create<UnreadState>()((set, get) => ({
  counts: new Map(),

  set(roomId, count) {
    set((s) => {
      const next = new Map(s.counts)
      next.set(roomId, count)
      return { counts: next }
    })
  },

  increment(roomId) {
    set((s) => {
      const next = new Map(s.counts)
      next.set(roomId, (next.get(roomId) ?? 0) + 1)
      return { counts: next }
    })
  },

  clear(roomId) {
    set((s) => {
      const next = new Map(s.counts)
      next.delete(roomId)
      return { counts: next }
    })
  },

  total() {
    let sum = 0
    for (const v of get().counts.values()) sum += v
    return sum
  },
}))
