import { apiJson, apiFetch } from './client'
import type { UserProfile } from '@/types'

export function getMe() {
  return apiJson<UserProfile>('/api/Me')
}

export function updateDisplayName(displayName: string) {
  return apiJson<void>('/api/Me/display-name', {
    method: 'PATCH',
    body: JSON.stringify({ displayName }),
  })
}

export function deleteAccount() {
  return apiFetch('/api/Me', { method: 'DELETE' })
}
