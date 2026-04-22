import { apiJson } from './client'

export interface UserSessionDto {
  id: string
  deviceInfo?: string | null
  userAgent?: string | null
  ipAddress?: string | null
  createdAt: string
  lastSeenAt: string
  isCurrent: boolean
}

export function registerSession(deviceInfo?: string) {
  return apiJson<{ id: string }>('/api/Sessions/register', {
    method: 'POST',
    body: JSON.stringify({ deviceInfo: deviceInfo ?? null }),
  })
}

export function listSessions() {
  return apiJson<UserSessionDto[]>('/api/Sessions')
}

export function revokeSession(id: string) {
  return apiJson<void>(`/api/Sessions/${id}`, { method: 'DELETE' })
}

export function revokeCurrentSession() {
  return apiJson<void>('/api/Sessions/current', { method: 'DELETE' })
}
