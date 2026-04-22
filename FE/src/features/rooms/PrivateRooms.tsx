import { useNavigate } from 'react-router'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { getMyRooms, getMyInvitations, acceptInvitation, rejectInvitation } from '@/api/rooms'
import { RoomVisibility } from '@/types'

export default function PrivateRooms() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const { data: myRooms, isLoading } = useQuery({
    queryKey: ['rooms', 'mine'],
    queryFn: getMyRooms,
  })

  const { data: invitations } = useQuery({
    queryKey: ['invitations', 'mine'],
    queryFn: getMyInvitations,
  })

  const privateRooms = (myRooms ?? []).filter((r) => r.visibility === RoomVisibility.Private)

  const acceptMutation = useMutation({
    mutationFn: (id: string) => acceptInvitation(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['invitations', 'mine'] })
      void queryClient.invalidateQueries({ queryKey: ['rooms', 'mine'] })
    },
  })

  const rejectMutation = useMutation({
    mutationFn: (id: string) => rejectInvitation(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['invitations', 'mine'] })
    },
  })

  return (
    <div className="p-6 max-w-3xl mx-auto">
      <h1 className="text-2xl font-bold mb-4">Private Rooms</h1>

      {isLoading && <div className="flex justify-center py-8"><span className="loading loading-spinner" /></div>}

      <div className="space-y-3 mb-8">
        {privateRooms.map((room) => (
          <div key={room.id} className="card card-compact bg-base-100 shadow">
            <div className="card-body flex-row items-center justify-between">
              <div>
                <h3 className="font-semibold">
                  # {room.name}
                  <span className="badge badge-outline badge-sm ml-2">{room.memberCount} members</span>
                </h3>
                {room.description && <p className="text-sm text-base-content/70">{room.description}</p>}
              </div>
              <button className="btn btn-sm btn-outline" onClick={() => navigate(`/rooms/${room.id}`)}>
                Open
              </button>
            </div>
          </div>
        ))}
        {privateRooms.length === 0 && !isLoading && (
          <p className="text-sm text-base-content/60">You are not a member of any private rooms.</p>
        )}
      </div>

      <h2 className="text-xl font-bold mb-4">Pending Invitations</h2>
      <div className="space-y-3">
        {(invitations ?? []).map((inv) => (
          <div key={inv.id} className="card card-compact bg-base-100 shadow">
            <div className="card-body flex-row items-center justify-between">
              <div>
                <h3 className="font-semibold"># {inv.roomName}</h3>
                <p className="text-sm text-base-content/70">Invited by {inv.invitedByUserName}</p>
              </div>
              <div className="flex gap-2">
                <button
                  className="btn btn-sm btn-primary"
                  disabled={acceptMutation.isPending}
                  onClick={() => acceptMutation.mutate(inv.id)}
                >
                  Accept
                </button>
                <button
                  className="btn btn-sm btn-ghost"
                  disabled={rejectMutation.isPending}
                  onClick={() => rejectMutation.mutate(inv.id)}
                >
                  Reject
                </button>
              </div>
            </div>
          </div>
        ))}
        {(!invitations || invitations.length === 0) && (
          <p className="text-sm text-base-content/60">No pending invitations.</p>
        )}
      </div>
    </div>
  )
}
