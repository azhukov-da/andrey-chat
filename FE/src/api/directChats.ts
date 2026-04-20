import { apiJson } from './client'
import type { Room } from '@/types'

export function openDirectChat(username: string) {
  return apiJson<Room>('/api/DirectChats', {
    method: 'POST',
    body: JSON.stringify({ username }),
  })
}
