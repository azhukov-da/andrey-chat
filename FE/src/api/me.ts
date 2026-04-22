import { apiJson } from './client'
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
  return apiJson<void>('/api/Me', { method: 'DELETE' })
}

export function changePassword(currentPassword: string, newPassword: string) {
  return apiJson<void>('/api/Me/password', {
    method: 'POST',
    body: JSON.stringify({ currentPassword, newPassword }),
  })
}
