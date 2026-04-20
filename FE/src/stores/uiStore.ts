import { create } from 'zustand'

interface UiState {
  activeRoomId: string | null
  sidebarCollapsed: boolean
  drafts: Map<string, string>
  replyTo: Map<string, string>
  setActiveRoom: (roomId: string | null) => void
  toggleSidebar: () => void
  setDraft: (roomId: string, text: string) => void
  getDraft: (roomId: string) => string
  setReplyTo: (roomId: string, messageId: string | null) => void
  getReplyTo: (roomId: string) => string | null
}

export const useUiStore = create<UiState>()((set, get) => ({
  activeRoomId: null,
  sidebarCollapsed: false,
  drafts: new Map(),
  replyTo: new Map(),

  setActiveRoom(roomId) {
    set({ activeRoomId: roomId })
  },

  toggleSidebar() {
    set((s) => ({ sidebarCollapsed: !s.sidebarCollapsed }))
  },

  setDraft(roomId, text) {
    set((s) => {
      const next = new Map(s.drafts)
      if (text) next.set(roomId, text)
      else next.delete(roomId)
      return { drafts: next }
    })
  },

  getDraft(roomId) {
    return get().drafts.get(roomId) ?? ''
  },

  setReplyTo(roomId, messageId) {
    set((s) => {
      const next = new Map(s.replyTo)
      if (messageId) next.set(roomId, messageId)
      else next.delete(roomId)
      return { replyTo: next }
    })
  },

  getReplyTo(roomId) {
    return get().replyTo.get(roomId) ?? null
  },
}))
