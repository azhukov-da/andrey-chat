import { NavLink } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { acceptInvitation, getMyInvitations, getMyRooms, rejectInvitation } from '@/api/rooms'
import { useUnreadStore } from '@/stores/unreadStore'
import { useUiStore } from '@/stores/uiStore'
import { RoomKind } from '@/types'
import PresenceDot from '@/features/chat/PresenceDot'
import { useAuthStore } from '@/stores/authStore'

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
  const me = useAuthStore((s) => s.me)

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

  if (collapsed) return null

  return (
    <aside className="w-64 border-l border-base-300 bg-base-100 flex flex-col overflow-y-auto">
      <div className="collapse collapse-arrow">
        <input type="checkbox" defaultChecked />
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
          <input type="checkbox" defaultChecked />
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
        <input type="checkbox" defaultChecked />
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
                  <PresenceDot userId={room.ownerId ?? ''} />
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
