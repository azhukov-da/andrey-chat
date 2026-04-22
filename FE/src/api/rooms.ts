import { apiJson } from './client'
import type { Room, Paged, RoomVisibilityValue } from '@/types'

export function getPublicRooms(search?: string, page = 1, pageSize = 20) {
  const params = new URLSearchParams({ page: String(page), pageSize: String(pageSize) })
  if (search) params.set('search', search)
  return apiJson<Paged<Room>>(`/api/Rooms/public?${params}`)
}

export function getMyRooms() {
  return apiJson<Room[]>('/api/Rooms/mine')
}

export function getRoom(id: string) {
  return apiJson<Room>(`/api/Rooms/${id}`)
}

export function createRoom(name: string, description: string | undefined, visibility: RoomVisibilityValue) {
  return apiJson<Room>('/api/Rooms', {
    method: 'POST',
    body: JSON.stringify({ name, description, visibility }),
  })
}

export function joinRoom(id: string) {
  return apiJson<void>(`/api/Rooms/${id}/join`, { method: 'POST' })
}

export function deleteRoom(id: string) {
  return apiJson<void>(`/api/Rooms/${id}`, { method: 'DELETE' })
}
