import { useEffect } from 'react'
import { useNavigate, useParams } from 'react-router'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useRoom } from '@/hooks/useRoom'
import { useUiStore } from '@/stores/uiStore'
import { deleteRoom } from '@/api/rooms'
import { RoomRole } from '@/types'
import MessageList from './MessageList'
import MessageComposer from './MessageComposer'

export default function ChatWindow() {
  const { id } = useParams<{ id: string }>()
  const { data: room, isLoading, isError } = useRoom(id)
  const setActiveRoom = useUiStore((s) => s.setActiveRoom)
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const deleteMutation = useMutation({
    mutationFn: (roomId: string) => deleteRoom(roomId),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['rooms', 'mine'] })
      void queryClient.invalidateQueries({ queryKey: ['rooms', 'public'] })
      navigate('/rooms')
    },
  })

  useEffect(() => {
    setActiveRoom(id ?? null)
    return () => setActiveRoom(null)
  }, [id, setActiveRoom])

  if (isLoading) {
    return (
      <div className="flex h-full items-center justify-center">
        <span className="loading loading-spinner loading-lg" />
      </div>
    )
  }

  if (isError || !room) {
    return (
      <div className="flex h-full items-center justify-center text-base-content/50">
        Room not found.
      </div>
    )
  }

  const isOwner = room.myRole === RoomRole.Owner

  const handleDelete = () => {
    if (!room) return
    const ok = window.confirm(`Delete room "${room.name}"? All messages will be permanently removed.`)
    if (!ok) return
    deleteMutation.mutate(room.id)
  }

  return (
    <div className="flex flex-col h-full">
      <div className="border-b border-base-300 px-6 py-3 bg-base-100 flex items-center justify-between">
        <div>
          <h2 className="font-bold text-lg"># {room.name}</h2>
          {room.description && <p className="text-sm text-base-content/60">{room.description}</p>}
        </div>
        {isOwner && (
          <button
            className="btn btn-sm btn-error btn-outline"
            onClick={handleDelete}
            disabled={deleteMutation.isPending}
            data-testid="delete-room-button"
          >
            {deleteMutation.isPending ? 'Deleting…' : 'Delete room'}
          </button>
        )}
      </div>
      <MessageList roomId={room.id} />
      <MessageComposer roomId={room.id} />
    </div>
  )
}
