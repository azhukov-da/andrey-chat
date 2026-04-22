import { useQuery } from '@tanstack/react-query'
import { listMembers } from '@/api/rooms'
import { RoomRole, type Room, type RoomMember } from '@/types'
import PresenceDot from './PresenceDot'

interface Props {
  room: Room
  isAdmin: boolean
  onInvite: () => void
  onManage: () => void
}

export default function RoomMembersPanel({ room, isAdmin, onInvite, onManage }: Props) {
  const membersQuery = useQuery({
    queryKey: ['rooms', room.id, 'members'],
    queryFn: () => listMembers(room.id),
    enabled: room.myRole != null,
  })

  const members: RoomMember[] = membersQuery.data ?? []
  const owner = members.find((m) => m.role === RoomRole.Owner)
  const admins = members.filter((m) => m.role === RoomRole.Admin)
  const regular = members.filter((m) => m.role === RoomRole.Member)

  return (
    <aside
      className="w-64 border-l border-base-300 bg-base-100 flex flex-col overflow-y-auto"
      data-testid="room-members-panel"
    >
      <div className="px-4 py-3 border-b border-base-300">
        <div className="font-semibold text-sm uppercase tracking-wide text-base-content/60">
          Room info
        </div>
        <div className="mt-2">
          <div className="font-bold"># {room.name}</div>
          {room.description && (
            <div className="text-xs text-base-content/60 mt-1">{room.description}</div>
          )}
          <div className="text-xs text-base-content/60 mt-1">
            {room.memberCount} member{room.memberCount === 1 ? '' : 's'}
          </div>
        </div>
      </div>

      <div className="px-4 py-3 border-b border-base-300">
        <div className="font-semibold text-sm uppercase tracking-wide text-base-content/60 mb-2">
          Owner
        </div>
        {owner ? (
          <div className="flex items-center gap-2 text-sm" data-testid="panel-owner">
            <PresenceDot userId={owner.userId} />
            <span className="truncate">{owner.displayName || owner.userName}</span>
          </div>
        ) : (
          <div className="text-xs text-base-content/60">Unknown</div>
        )}
      </div>

      <div className="px-4 py-3 border-b border-base-300">
        <div className="font-semibold text-sm uppercase tracking-wide text-base-content/60 mb-2">
          Admins ({admins.length})
        </div>
        {admins.length === 0 ? (
          <div className="text-xs text-base-content/60">No admins</div>
        ) : (
          <ul className="space-y-1" data-testid="panel-admins">
            {admins.map((m) => (
              <li key={m.userId} className="flex items-center gap-2 text-sm">
                <PresenceDot userId={m.userId} />
                <span className="truncate">{m.displayName || m.userName}</span>
              </li>
            ))}
          </ul>
        )}
      </div>

      <div className="px-4 py-3 border-b border-base-300 flex-1">
        <div className="font-semibold text-sm uppercase tracking-wide text-base-content/60 mb-2">
          Members ({regular.length})
        </div>
        {membersQuery.isLoading && <div className="loading loading-spinner loading-sm" />}
        {membersQuery.isError && (
          <div className="alert alert-error text-xs">Failed to load members.</div>
        )}
        {!membersQuery.isLoading && regular.length === 0 && (
          <div className="text-xs text-base-content/60">No other members</div>
        )}
        <ul className="space-y-1" data-testid="panel-members">
          {regular.map((m) => (
            <li key={m.userId} className="flex items-center gap-2 text-sm">
              <PresenceDot userId={m.userId} />
              <span className="truncate">{m.displayName || m.userName}</span>
            </li>
          ))}
        </ul>
      </div>

      {isAdmin && (
        <div className="px-4 py-3 border-t border-base-300 flex flex-col gap-2">
          <button
            className="btn btn-sm btn-outline w-full"
            onClick={onInvite}
            data-testid="panel-invite-user"
          >
            Invite user
          </button>
          <button
            className="btn btn-sm btn-outline w-full"
            onClick={onManage}
            data-testid="panel-manage-room"
          >
            Manage room
          </button>
        </div>
      )}
    </aside>
  )
}
