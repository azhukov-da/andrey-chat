import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { getFriends, acceptFriendRequest, blockUser } from '@/api/friends'
import { openDirectChat } from '@/api/directChats'
import { FriendshipStatus } from '@/types'
import FriendRequestDialog from './FriendRequestDialog'
import PresenceDot from '@/features/chat/PresenceDot'

export default function FriendList() {
  const [showAdd, setShowAdd] = useState(false)
  const { data: friends = [], isLoading } = useQuery({ queryKey: ['friends'], queryFn: getFriends })
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  const acceptMutation = useMutation({
    mutationFn: (userId: string) => acceptFriendRequest(userId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['friends'] }),
  })

  const blockMutation = useMutation({
    mutationFn: (userId: string) => blockUser(userId),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: ['friends'] }),
  })

  const dmMutation = useMutation({
    mutationFn: (username: string) => openDirectChat(username),
    onSuccess: (room) => navigate(`/rooms/${room.id}`),
  })

  const accepted = friends.filter((f) => f.status === FriendshipStatus.Accepted)
  const pending = friends.filter((f) => f.status === FriendshipStatus.Pending)

  return (
    <div className="p-6 max-w-2xl mx-auto">
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-bold">Contacts</h1>
        <button className="btn btn-primary btn-sm" onClick={() => setShowAdd(true)}>+ Add Contact</button>
      </div>

      {isLoading && <div className="flex justify-center py-8"><span className="loading loading-spinner" /></div>}

      {pending.length > 0 && (
        <div className="mb-6">
          <h2 className="font-semibold text-sm uppercase tracking-wide text-base-content/60 mb-2">Pending Requests</h2>
          <div className="space-y-2">
            {pending.map((f) => (
              <div key={f.userId} className="flex items-center justify-between p-3 bg-base-100 rounded-lg shadow-sm">
                <div className="flex items-center gap-2">
                  <PresenceDot userId={f.userId} />
                  <span>{f.displayName ?? f.userName}</span>
                </div>
                <div className="flex gap-2">
                  <button className="btn btn-xs btn-success" onClick={() => acceptMutation.mutate(f.userId)}>Accept</button>
                  <button className="btn btn-xs btn-error btn-outline" onClick={() => blockMutation.mutate(f.userId)}>Block</button>
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      <div>
        <h2 className="font-semibold text-sm uppercase tracking-wide text-base-content/60 mb-2">Friends</h2>
        {accepted.length === 0 && !isLoading && (
          <p className="text-base-content/50 text-sm">No friends yet. Add someone!</p>
        )}
        <div className="space-y-2">
          {accepted.map((f) => (
            <div key={f.userId} className="flex items-center justify-between p-3 bg-base-100 rounded-lg shadow-sm">
              <div className="flex items-center gap-2">
                <PresenceDot userId={f.userId} />
                <span>{f.displayName ?? f.userName}</span>
                <span className="text-xs text-base-content/50">@{f.userName}</span>
              </div>
              <button
                className="btn btn-xs btn-outline"
                onClick={() => dmMutation.mutate(f.userName)}
                disabled={dmMutation.isPending}
              >
                Message
              </button>
            </div>
          ))}
        </div>
      </div>

      {showAdd && <FriendRequestDialog onClose={() => setShowAdd(false)} />}
    </div>
  )
}
