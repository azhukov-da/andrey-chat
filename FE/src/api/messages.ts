import { apiJson } from './client'
import type { Message, CursorPaged } from '@/types'

export function getMessages(roomId: string, before?: string, limit = 50) {
  const params = new URLSearchParams({ limit: String(limit) })
  if (before) params.set('before', before)
  return apiJson<CursorPaged<Message>>(`/api/rooms/${roomId}/Messages?${params}`)
}

export function sendMessage(roomId: string, text: string, replyToMessageId?: string) {
  return apiJson<Message>(`/api/rooms/${roomId}/Messages`, {
    method: 'POST',
    body: JSON.stringify({ text, replyToMessageId: replyToMessageId ?? null }),
  })
}

export function editMessage(id: string, text: string) {
  return apiJson<void>(`/api/messages/${id}`, {
    method: 'PATCH',
    body: JSON.stringify({ text }),
  })
}

export function deleteMessage(id: string) {
  return apiJson<void>(`/api/messages/${id}`, { method: 'DELETE' })
}

export function markRead(roomId: string, messageId: string) {
  return apiJson<void>(`/api/rooms/${roomId}/Messages/read`, {
    method: 'POST',
    body: JSON.stringify({ messageId }),
  })
}
