import { NavLink } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { getMyRooms } from '@/api/rooms'
import { useUnreadStore } from '@/stores/unreadStore'
import { useUiStore } from '@/stores/uiStore'
import { RoomKind } from '@/types'
import PresenceDot from '@/features/chat/PresenceDot'
import { useAuthStore } from '@/stores/authStore'

export default function RightSidebar() {
  const { data: rooms = [] } = useQuery({ queryKey: ['rooms', 'mine'], queryFn: getMyRooms })
  const unreadCounts = useUnreadStore((s) => s.counts)
  const collapsed = useUiStore((s) => s.sidebarCollapsed)
  const me = useAuthStore((s) => s.me)

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
