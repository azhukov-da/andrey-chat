import { useCallback } from 'react'
import { useNavigate } from 'react-router'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useAuthStore } from '@/stores/authStore'
import * as authApi from '@/api/auth'
import * as meApi from '@/api/me'

export function useMe() {
  const accessToken = useAuthStore((s) => s.accessToken)
  return useQuery({
    queryKey: ['me'],
    queryFn: meApi.getMe,
    enabled: !!accessToken,
    retry: false,
  })
}

export function useAuth() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { setTokens, setMe, setKeepSignedIn, clearAuth, hydrate } = useAuthStore()

  const login = useCallback(
    async (email: string, password: string, keepSignedIn: boolean) => {
      setKeepSignedIn(keepSignedIn)
      const tokens = await authApi.login(email, password)
      setTokens(tokens.accessToken, tokens.refreshToken)
      const me = await meApi.getMe()
      setMe(me)
      queryClient.setQueryData(['me'], me)
    },
    [setTokens, setMe, setKeepSignedIn, queryClient]
  )

  const logout = useCallback(() => {
    clearAuth()
    queryClient.clear()
    navigate('/login')
  }, [clearAuth, queryClient, navigate])

  const register = useCallback(async (email: string, password: string) => {
    await authApi.register(email, password)
  }, [])

  const bootstrapAuth = useCallback(async () => {
    hydrate()
    const { accessToken, refreshToken } = useAuthStore.getState()
    if (!accessToken && !refreshToken) return false

    try {
      const me = await meApi.getMe()
      setMe(me)
      queryClient.setQueryData(['me'], me)
      return true
    } catch {
      if (refreshToken) {
        try {
          const tokens = await authApi.refresh(refreshToken)
          setTokens(tokens.accessToken, tokens.refreshToken)
          const me = await meApi.getMe()
          setMe(me)
          queryClient.setQueryData(['me'], me)
          return true
        } catch {
          clearAuth()
          return false
        }
      }
      clearAuth()
      return false
    }
  }, [hydrate, setMe, setTokens, clearAuth, queryClient])

  return { login, logout, register, bootstrapAuth }
}
