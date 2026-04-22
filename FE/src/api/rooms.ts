import { apiJson } from './client'
import type { Room, Paged, RoomVisibilityValue, RoomMember } from '@/types'

export interface BannedRoomUser {
  bannedUserId: string
  bannedUserName: string
  bannedByUserId?: string | null
  bannedByUserName?: string | null
  reason?: string | null
  createdAt: string
}

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

export function updateRoom(
  id: string,
  name: string,
  description: string | undefined,
  visibility: RoomVisibilityValue,
) {
  return apiJson<Room>(`/api/Rooms/${id}`, {
    method: 'PUT',
    body: JSON.stringify({ name, description, visibility }),
  })
}

export function listMembers(id: string) {
  return apiJson<RoomMember[]>(`/api/Rooms/${id}/members`)
}

export function makeAdmin(roomId: string, userId: string) {
  return apiJson<void>(`/api/Rooms/${roomId}/members/${userId}/make-admin`, { method: 'POST' })
}

export function removeAdmin(roomId: string, userId: string) {
  return apiJson<void>(`/api/Rooms/${roomId}/members/${userId}/remove-admin`, { method: 'POST' })
}

export function removeMember(roomId: string, userId: string) {
  return apiJson<void>(`/api/Rooms/${roomId}/members/${userId}`, { method: 'DELETE' })
}

export function banMember(roomId: string, userId: string, reason?: string) {
  return apiJson<void>(`/api/Rooms/${roomId}/bans/${userId}`, {
    method: 'POST',
    body: JSON.stringify({ reason: reason ?? null }),
  })
}

export function unbanMember(roomId: string, userId: string) {
  return apiJson<void>(`/api/Rooms/${roomId}/bans/${userId}`, { method: 'DELETE' })
}

export function listBanned(roomId: string) {
  return apiJson<BannedRoomUser[]>(`/api/Rooms/${roomId}/bans`)
}

export function leaveRoom(id: string) {
  return apiJson<void>(`/api/Rooms/${id}/leave`, { method: 'POST' })
}

export interface RoomInvitation {
  id: string
  roomId: string
  roomName: string
  invitedUserId: string
  invitedUserName: string
  invitedByUserId: string
  invitedByUserName: string
  status: number
  createdAt: string
}

export function inviteUserToRoom(roomId: string, inviteeUsername: string) {
  return apiJson<RoomInvitation>(`/api/Rooms/${roomId}/invitations`, {
    method: 'POST',
    body: JSON.stringify({ inviteeUsername }),
  })
}

export function getMyInvitations() {
  return apiJson<RoomInvitation[]>('/api/me/invitations')
}

export function acceptInvitation(id: string) {
  return apiJson<void>(`/api/invitations/${id}/accept`, { method: 'POST' })
}

export function rejectInvitation(id: string) {
  return apiJson<void>(`/api/invitations/${id}/reject`, { method: 'POST' })
}
