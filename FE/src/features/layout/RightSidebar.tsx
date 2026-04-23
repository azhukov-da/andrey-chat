import { useEffect, useMemo, useState } from 'react'
import { NavLink } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { acceptInvitation, getMyInvitations, getMyRooms, rejectInvitation } from '@/api/rooms'
import { useUnreadStore } from '@/stores/unreadStore'
import { useUiStore } from '@/stores/uiStore'
import { RoomKind, type PresenceStatus } from '@/types'
import PresenceDot from '@/features/chat/PresenceDot'
import { useAuthStore } from '@/stores/authStore'
import { getHubConnection } from '@/realtime/hubClient'
import { usePresenceStore } from '@/stores/presenceStore'
import * as signalR from '@microsoft/signalr'

export default function RightSidebar() {
  const queryClient = useQueryClient()
  const { data: rooms = [] } = useQuery({ queryKey: ['rooms', 'mine'], queryFn: getMyRooms })
  const { data: invitations = [] } = useQuery({
    queryKey: ['invitations', 'mine'],
    queryFn: getMyInvitations,
    refetchInterval: 30000,
  })
  const unreadCounts = useUnreadStore((s) => s.counts)
  const collapsed = useUiStore((s) => s.sidebarCollapsed)
  const activeRoomId = useUiStore((s) => s.activeRoomId)
  const me = useAuthStore((s) => s.me)

  // Auto-compaction: when a room is active, collapse all accordion sections.
  // User can still click to expand individual sections.
  const [roomsOpen, setRoomsOpen] = useState(true)
  const [dmsOpen, setDmsOpen] = useState(true)
  const [invitationsOpen, setInvitationsOpen] = useState(true)

  useEffect(() => {
    if (activeRoomId) {
      setRoomsOpen(false)
      setDmsOpen(false)
      setInvitationsOpen(false)
    } else {
      setRoomsOpen(true)
      setDmsOpen(true)
      setInvitationsOpen(true)
    }
  }, [activeRoomId])

  const acceptMut = useMutation({
    mutationFn: (id: string) => acceptInvitation(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['invitations', 'mine'] })
      void queryClient.invalidateQueries({ queryKey: ['rooms', 'mine'] })
    },
  })
  const rejectMut = useMutation({
    mutationFn: (id: string) => rejectInvitation(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['invitations', 'mine'] })
    },
  })

  const groupRooms = rooms.filter((r) => r.kind === RoomKind.Group)
  const directRooms = rooms.filter((r) => r.kind === RoomKind.Direct)

  // Hydrate presence for direct-message partners.
  const presenceUserIdsKey = useMemo(() => {
    const ids = new Set<string>()
    for (const r of directRooms) if (r.otherUserId) ids.add(r.otherUserId)
    return Array.from(ids).sort().join(',')
  }, [directRooms])

  useEffect(() => {
    if (!presenceUserIdsKey) return
    const userIds = presenceUserIdsKey.split(',')
    let cancelled = false

    const hydrate = async () => {
      const hub = getHubConnection()
      for (let i = 0; i < 20 && hub.state !== signalR.HubConnectionState.Connected; i++) {
        await new Promise((r) => setTimeout(r, 500))
        if (cancelled) return
      }
      if (cancelled || hub.state !== signalR.HubConnectionState.Connected) return
      try {
        const result = await hub.invoke<Record<string, string>>('GetPresenceFor', userIds)
        if (cancelled || !result) return
        const update = usePresenceStore.getState().update
        for (const [userId, status] of Object.entries(result)) {
          update(userId, status.toLowerCase() as PresenceStatus)
        }
      } catch {
        // ignore — presence events will eventually sync state
      }
    }

    void hydrate()
    const hub = getHubConnection()
    const onReconnected = () => { void hydrate() }
    hub.onreconnected(onReconnected)
    return () => { cancelled = true }
  }, [presenceUserIdsKey])

  if (collapsed) return null

  return (
    <aside
      className="w-64 border-l border-base-300 bg-base-100 flex flex-col overflow-y-auto"
      data-testid="right-sidebar"
      data-compact={activeRoomId ? 'true' : 'false'}
    >
      <div className="collapse collapse-arrow">
        <input
          type="checkbox"
          checked={roomsOpen}
          onChange={(e) => setRoomsOpen(e.target.checked)}
          data-testid="sidebar-rooms-toggle"
        />
        <div className="collapse-title font-semibold text-sm uppercase tracking-wide text-base-content/60 px-4 py-3">
          Rooms
        </div>
        <div className="collapse-content px-0 pb-0">
          {groupRooms.map((room) => {
            const unread = unreadCounts.get(room.id) ?? 0
            return (
              <NavLink
                key={room.id}
                to={`/rooms/${room.id}`}
                className={({ isActive }) =>
                  `flex items-center justify-between px-4 py-2 text-sm hover:bg-base-200 ${isActive ? 'bg-base-200 font-semibold' : ''}`
                }
              >
                <span className="truncate"># {room.name}</span>
                {unread > 0 && <span className="badge badge-primary badge-sm">{unread}</span>}
              </NavLink>
            )
          })}
        </div>
      </div>

      {invitations.length > 0 && (
        <div className="collapse collapse-arrow">
          <input
            type="checkbox"
            checked={invitationsOpen}
            onChange={(e) => setInvitationsOpen(e.target.checked)}
          />
          <div className="collapse-title font-semibold text-sm uppercase tracking-wide text-base-content/60 px-4 py-3">
            Invitations ({invitations.length})
          </div>
          <div className="collapse-content px-0 pb-2">
            {invitations.map((inv) => (
              <div key={inv.id} className="px-4 py-2 text-sm border-b border-base-200" data-testid="invitation-item">
                <div className="truncate">
                  <span className="font-semibold"># {inv.roomName}</span>
                </div>
                <div className="text-xs text-base-content/60 truncate">from {inv.invitedByUserName}</div>
                <div className="flex gap-1 mt-1">
                  <button
                    className="btn btn-xs btn-primary"
                    onClick={() => acceptMut.mutate(inv.id)}
                    disabled={acceptMut.isPending}
                    data-testid="invitation-accept"
                  >
                    Accept
                  </button>
                  <button
                    className="btn btn-xs btn-ghost"
                    onClick={() => rejectMut.mutate(inv.id)}
                    disabled={rejectMut.isPending}
                    data-testid="invitation-reject"
                  >
                    Reject
                  </button>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      <div className="collapse collapse-arrow">
        <input
          type="checkbox"
          checked={dmsOpen}
          onChange={(e) => setDmsOpen(e.target.checked)}
          data-testid="sidebar-dms-toggle"
        />
        <div className="collapse-title font-semibold text-sm uppercase tracking-wide text-base-content/60 px-4 py-3">
          Direct Messages
        </div>
        <div className="collapse-content px-0 pb-0">
          {directRooms.map((room) => {
            const unread = unreadCounts.get(room.id) ?? 0
            const otherName = room.name.replace(me?.userName ?? '', '').replace('/', '').trim() || room.name
            return (
              <NavLink
                key={room.id}
                to={`/rooms/${room.id}`}
                className={({ isActive }) =>
                  `flex items-center justify-between px-4 py-2 text-sm hover:bg-base-200 ${isActive ? 'bg-base-200 font-semibold' : ''}`
                }
              >
                <div className="flex items-center gap-2 min-w-0">
                  <PresenceDot userId={room.otherUserId ?? ''} />
                  <span className="truncate">{otherName}</span>
                </div>
                {unread > 0 && <span className="badge badge-primary badge-sm">{unread}</span>}
              </NavLink>
            )
          })}
        </div>
      </div>

    </aside>
  )
}
