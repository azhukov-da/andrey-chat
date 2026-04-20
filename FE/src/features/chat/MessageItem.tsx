import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { editMessage, deleteMessage } from '@/api/messages'
import { formatTime } from '@/lib/formatTime'
import { useUiStore } from '@/stores/uiStore'
import type { Message } from '@/types'

interface Props {
  message: Message
  isMine: boolean
  roomId: string
}

export default function MessageItem({ message, isMine, roomId }: Props) {
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [editText, setEditText] = useState(message.text)
  const setReplyTo = useUiStore((s) => s.setReplyTo)

  type Pages = { pages: { items: Message[] }[] }

  const editMutation = useMutation({
    mutationFn: (text: string) => editMessage(message.id, text),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['messages', roomId] })
      setEditing(false)
    },
  })

  const deleteMutation = useMutation({
    mutationFn: () => deleteMessage(message.id),
    onSuccess: () => {
      queryClient.setQueryData<Pages>(['messages', roomId], (old) => {
        if (!old) return old
        return {
          ...old,
          pages: old.pages.map((p) => ({
            ...p,
            items: p.items.map((m) => (m.id === message.id ? { ...m, isDeleted: true, text: '' } : m)),
          })),
        }
      })
    },
  })

  if (message.isDeleted) {
    return (
      <div className={`chat ${isMine ? 'chat-end' : 'chat-start'} px-4`}>
        <div className="chat-bubble chat-bubble-ghost italic opacity-50 text-sm">Message deleted</div>
      </div>
    )
  }

  return (
    <div className={`chat ${isMine ? 'chat-end' : 'chat-start'} px-4 py-1`}>
      {!isMine && (
        <div className="chat-header text-xs opacity-60 mb-0.5">
          {message.authorDisplayName ?? message.authorUserName}
        </div>
      )}
      <div className="chat-bubble max-w-sm break-words">
        {editing ? (
          <div className="flex gap-2">
            <input
              className="input input-xs input-bordered flex-1"
              value={editText}
              onChange={(e) => setEditText(e.target.value)}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault()
                  if (editText.trim()) editMutation.mutate(editText.trim())
                }
                if (e.key === 'Escape') setEditing(false)
              }}
              autoFocus
            />
            <button className="btn btn-xs btn-primary" onClick={() => editMutation.mutate(editText.trim())}>Save</button>
            <button className="btn btn-xs btn-ghost" onClick={() => setEditing(false)}>✕</button>
          </div>
        ) : (
          message.text
        )}
      </div>
      <div className="chat-footer opacity-50 text-xs flex gap-2 mt-0.5">
        <span>{formatTime(message.createdAt)}</span>
        {message.editedAt && <span>edited</span>}
        <button className="hover:opacity-100" onClick={() => setReplyTo(roomId, message.id)}>↩</button>
        {isMine && !editing && (
          <>
            <button className="hover:opacity-100" onClick={() => setEditing(true)}>✏</button>
            <button className="hover:opacity-100 text-error" onClick={() => deleteMutation.mutate()}>✕</button>
          </>
        )}
      </div>
    </div>
  )
}
