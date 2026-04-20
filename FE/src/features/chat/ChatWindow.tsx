import { useEffect } from 'react'
import { useParams } from 'react-router'
import { useRoom } from '@/hooks/useRoom'
import { useUiStore } from '@/stores/uiStore'
import MessageList from './MessageList'
import MessageComposer from './MessageComposer'

export default function ChatWindow() {
  const { id } = useParams<{ id: string }>()
  const { data: room, isLoading, isError } = useRoom(id)
  const setActiveRoom = useUiStore((s) => s.setActiveRoom)

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

  return (
    <div className="flex flex-col h-full">
      <div className="border-b border-base-300 px-6 py-3 bg-base-100">
        <h2 className="font-bold text-lg"># {room.name}</h2>
        {room.description && <p className="text-sm text-base-content/60">{room.description}</p>}
      </div>
      <MessageList roomId={room.id} />
      <MessageComposer roomId={room.id} />
    </div>
  )
}
