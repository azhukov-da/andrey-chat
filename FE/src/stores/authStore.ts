import { create } from 'zustand'
import type { UserProfile } from '@/types'

interface AuthState {
  accessToken: string | null
  refreshToken: string | null
  me: UserProfile | null
  keepSignedIn: boolean
  setTokens: (accessToken: string, refreshToken: string) => void
  setMe: (me: UserProfile) => void
  setKeepSignedIn: (keep: boolean) => void
  clearAuth: () => void
  hydrate: () => void
}

function loadFromStorage(): { accessToken: string | null; refreshToken: string | null; keepSignedIn: boolean } {
  const accessToken = sessionStorage.getItem('accessToken')
  const refreshToken = localStorage.getItem('refreshToken') ?? sessionStorage.getItem('refreshToken')
  const keepSignedIn = !!localStorage.getItem('refreshToken')
  return { accessToken, refreshToken, keepSignedIn }
}

export const useAuthStore = create<AuthState>()((set, get) => ({
  accessToken: null,
  refreshToken: null,
  me: null,
  keepSignedIn: false,

  setTokens(accessToken, refreshToken) {
    sessionStorage.setItem('accessToken', accessToken)
    if (get().keepSignedIn) {
      localStorage.setItem('refreshToken', refreshToken)
    } else {
      sessionStorage.setItem('refreshToken', refreshToken)
    }
    set({ accessToken, refreshToken })
  },

  setMe(me) {
    set({ me })
  },

  setKeepSignedIn(keep) {
    set({ keepSignedIn: keep })
  },

  clearAuth() {
    sessionStorage.removeItem('accessToken')
    sessionStorage.removeItem('refreshToken')
    localStorage.removeItem('refreshToken')
    set({ accessToken: null, refreshToken: null, me: null })
  },

  hydrate() {
    const { accessToken, refreshToken, keepSignedIn } = loadFromStorage()
    set({ accessToken, refreshToken, keepSignedIn })
  },
}))
