import { useAuthStore } from '@/stores/authStore'

const AUTH_LOGOUT_EVENT = 'auth/logout'

let refreshPromise: Promise<string | null> | null = null

async function refreshAccessToken(): Promise<string | null> {
  if (refreshPromise) return refreshPromise

  refreshPromise = (async () => {
    const { refreshToken, setTokens, clearAuth } = useAuthStore.getState()
    if (!refreshToken) {
      clearAuth()
      window.dispatchEvent(new Event(AUTH_LOGOUT_EVENT))
      return null
    }
    try {
      const res = await fetch('/refresh', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken }),
      })
      if (!res.ok) throw new Error('refresh failed')
      const data = (await res.json()) as { accessToken: string; refreshToken: string; expiresIn: number }
      setTokens(data.accessToken, data.refreshToken)
      return data.accessToken
    } catch {
      clearAuth()
      window.dispatchEvent(new Event(AUTH_LOGOUT_EVENT))
      return null
    } finally {
      refreshPromise = null
    }
  })()

  return refreshPromise
}

export async function apiFetch(input: string, init: RequestInit = {}): Promise<Response> {
  const { accessToken } = useAuthStore.getState()

  const headers = new Headers(init.headers)
  if (accessToken) headers.set('Authorization', `Bearer ${accessToken}`)
  if (!headers.has('Content-Type') && !(init.body instanceof FormData)) {
    headers.set('Content-Type', 'application/json')
  }

  let res = await fetch(input, { ...init, headers })

  if (res.status === 401) {
    const newToken = await refreshAccessToken()
    if (newToken) {
      headers.set('Authorization', `Bearer ${newToken}`)
      res = await fetch(input, { ...init, headers })
    }
  }

  return res
}

export async function apiJson<T>(input: string, init: RequestInit = {}): Promise<T> {
  const res = await apiFetch(input, init)
  if (!res.ok) {
    let errorText = res.statusText
    try {
      const body = (await res.json()) as { title?: string; detail?: string; errors?: Record<string, string[]> }
      errorText = body.title ?? body.detail ?? Object.values(body.errors ?? {}).flat().join(', ') ?? errorText
    } catch { /* ignore */ }
    throw new ApiError(res.status, errorText)
  }
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

export class ApiError extends Error {
  constructor(public status: number, message: string) {
    super(message)
  }
}
