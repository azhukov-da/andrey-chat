import { apiJson } from './client'
import type { Friend } from '@/types'

export function getFriends() {
  return apiJson<Friend[]>('/api/Friends')
}

export function sendFriendRequest(username: string, message?: string) {
  return apiJson<void>('/api/Friends/requests', {
    method: 'POST',
    body: JSON.stringify({ username, message: message ?? null }),
  })
}

export function acceptFriendRequest(userId: string) {
  return apiJson<void>(`/api/Friends/requests/${userId}/accept`, { method: 'POST' })
}

export function blockUser(userId: string) {
  return apiJson<void>(`/api/Friends/blocks/${userId}`, { method: 'POST' })
}

export function unblockUser(userId: string) {
  return apiJson<void>(`/api/Friends/blocks/${userId}`, { method: 'DELETE' })
}
