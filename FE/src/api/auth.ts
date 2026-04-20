import type { AccessTokenResponse } from '@/types'

async function postJson<T>(url: string, body: unknown): Promise<T> {
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  if (!res.ok) {
    let errorText = res.statusText
    try {
      const data = (await res.json()) as { title?: string; detail?: string; errors?: Record<string, string[]> }
      errorText = (data.title ?? data.detail ?? Object.values(data.errors ?? {}).flat().join(', ')) || errorText
    } catch { /* ignore */ }
    throw new Error(errorText)
  }
  if (res.status === 200 || res.status === 201) return res.json() as Promise<T>
  return undefined as T
}

export function register(email: string, password: string) {
  return postJson<void>('/register', { email, password })
}

export function login(email: string, password: string): Promise<AccessTokenResponse> {
  return postJson<AccessTokenResponse>('/login', { email, password })
}

export function refresh(refreshToken: string): Promise<AccessTokenResponse> {
  return postJson<AccessTokenResponse>('/refresh', { refreshToken })
}

export function forgotPassword(email: string) {
  return postJson<void>('/forgotPassword', { email })
}

export function resetPassword(email: string, resetCode: string, newPassword: string) {
  return postJson<void>('/resetPassword', { email, resetCode, newPassword })
}
