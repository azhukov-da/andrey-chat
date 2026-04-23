import { useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { editMessage, deleteMessage } from '@/api/messages'
import { downloadAttachment, loadAttachmentObjectUrl } from '@/api/attachments'
import { formatTime } from '@/lib/formatTime'
import { useUiStore } from '@/stores/uiStore'
import type { Message, AttachmentMetadata } from '@/types'

interface Props {
  message: Message
  isMine: boolean
  roomId: string
  replyTo?: Message
}

export default function MessageItem({ message, isMine, roomId, replyTo }: Props) {
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
        {message.replyToMessageId && (
          <div
            className="border-l-4 border-primary/60 bg-base-200/50 rounded pl-2 pr-2 py-1 mb-1 text-xs opacity-80"
            data-testid="reply-quote"
          >
            {replyTo ? (
              <>
                <div className="font-semibold opacity-90">
                  {replyTo.authorDisplayName ?? replyTo.authorUserName}
                </div>
                <div className="italic truncate whitespace-pre-wrap line-clamp-2 max-w-[16rem]">
                  {replyTo.isDeleted
                    ? 'Message deleted'
                    : replyTo.text && replyTo.text.length > 0
                    ? replyTo.text
                    : replyTo.attachments && replyTo.attachments[0]
                    ? `[attachment: ${replyTo.attachments[0].fileName}]`
                    : ''}
                </div>
              </>
            ) : (
              <div className="italic opacity-70">Replying to earlier message</div>
            )}
          </div>
        )}
        {message.attachments && message.attachments.length > 0 && (
          <div className="flex flex-col gap-2 mb-2" data-testid="attachments">
            {message.attachments.map((a) => (
              <AttachmentView key={a.id} attachment={a} />
            ))}
          </div>
        )}
        {editing ? (
          <div className="flex gap-2">
            <input
              className="input input-xs input-bordered flex-1 bg-base-100 text-base-content"
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

function AttachmentView({ attachment }: { attachment: AttachmentMetadata }) {
  const isImage = attachment.kind === 'Image'
  const [imgUrl, setImgUrl] = useState<string | null>(null)
  const [imgError, setImgError] = useState(false)

  useEffect(() => {
    if (!isImage) return
    let revokeUrl: string | null = null
    let cancelled = false
    loadAttachmentObjectUrl(attachment.id)
      .then((url) => {
        if (cancelled) {
          URL.revokeObjectURL(url)
          return
        }
        revokeUrl = url
        setImgUrl(url)
      })
      .catch(() => setImgError(true))
    return () => {
      cancelled = true
      if (revokeUrl) URL.revokeObjectURL(revokeUrl)
    }
  }, [attachment.id, isImage])

  const handleDownload = () => {
    void downloadAttachment(attachment.id, attachment.fileName).catch(() => {})
  }

  const sizeKb = (attachment.sizeBytes / 1024).toFixed(1)

  return (
    <div className="border border-base-300 rounded p-2 bg-base-200" data-testid="attachment-item">
      {isImage && imgUrl && !imgError && (
        <img src={imgUrl} alt={attachment.fileName} className="max-h-48 rounded mb-1" />
      )}
      <div className="flex items-center gap-2 text-sm">
        <span className="opacity-70">{isImage ? '🖼' : '📄'}</span>
        <button
          type="button"
          className="link link-primary truncate"
          onClick={handleDownload}
          title={`Download ${attachment.fileName}`}
          data-testid="attachment-download"
        >
          {attachment.fileName}
        </button>
        <span className="opacity-50 text-xs whitespace-nowrap">{sizeKb} KB</span>
      </div>
      {attachment.comment && (
        <div className="text-xs opacity-70 mt-1">{attachment.comment}</div>
      )}
    </div>
  )
}
