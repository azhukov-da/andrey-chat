import { useRef, useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { sendMessage } from '@/api/messages'
import { uploadAttachment } from '@/api/attachments'
import { useUiStore } from '@/stores/uiStore'
import { MAX_MESSAGE_BYTES } from '@/types'
import { getHubConnection } from '@/realtime/hubClient'
import type { Message } from '@/types'

interface Props {
  roomId: string
}

export default function MessageComposer({ roomId }: Props) {
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const fileInputRef = useRef<HTMLInputElement>(null)
  const queryClient = useQueryClient()
  const { getDraft, setDraft, setReplyTo } = useUiStore()
  const [text, setText] = useState(() => getDraft(roomId))
  const [uploadError, setUploadError] = useState<string | null>(null)
  const replyToId = useUiStore((s) => s.replyTo.get(roomId) ?? null)

  const byteCount = new TextEncoder().encode(text).length
  const overLimit = byteCount > MAX_MESSAGE_BYTES

  type Pages = { pages: { items: Message[]; nextCursor?: string | null }[] }

  const insertMessage = (msg: Message) => {
    queryClient.setQueryData<Pages>(['messages', roomId], (old) => {
      if (!old) return old
      if (old.pages.some((page) => page.items.some((m) => m.id === msg.id))) return old
      const [first, ...rest] = old.pages
      if (!first) return old
      return { ...old, pages: [{ ...first, items: [msg, ...first.items] }, ...rest] }
    })
  }

  const mutation = useMutation({
    mutationFn: (t: string) => sendMessage(roomId, t, replyToId ?? undefined),
    onSuccess: insertMessage,
  })

  const uploadMutation = useMutation({
    mutationFn: ({ file, comment }: { file: File; comment: string | undefined }) =>
      uploadAttachment(roomId, file, comment),
    onSuccess: (msg) => {
      insertMessage(msg)
      setUploadError(null)
    },
    onError: (e: Error) => setUploadError(e.message),
  })

  useEffect(() => {
    setDraft(roomId, text)
  }, [text, roomId, setDraft])

  const submit = () => {
    const trimmed = text.trim()
    if (!trimmed || overLimit || mutation.isPending) return
    mutation.mutate(trimmed)
    setText('')
    setReplyTo(roomId, null)

    getHubConnection().invoke('StopTyping', roomId).catch(() => {})
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      submit()
    }
    if (text.length > 0) {
      getHubConnection().invoke('StartTyping', roomId).catch(() => {})
    }
  }

  const uploadFiles = (files: File[], comment?: string) => {
    setUploadError(null)
    for (const file of files) {
      uploadMutation.mutate({ file, comment })
    }
  }

  const handlePaste = (e: React.ClipboardEvent<HTMLTextAreaElement>) => {
    const files = Array.from(e.clipboardData.files)
    if (files.length > 0) {
      e.preventDefault()
      uploadFiles(files, text.trim() || undefined)
      setText('')
    }
  }

  const handleFilesPicked = (e: React.ChangeEvent<HTMLInputElement>) => {
    const files = Array.from(e.target.files ?? [])
    if (files.length > 0) {
      uploadFiles(files, text.trim() || undefined)
      setText('')
    }
    if (fileInputRef.current) fileInputRef.current.value = ''
  }

  return (
    <div className="border-t border-base-300 bg-base-100 p-3">
      {replyToId && (
        <div className="border-l-4 border-primary pl-2 text-sm opacity-70 mb-2 flex items-center justify-between">
          <span>Replying to message</span>
          <button className="btn btn-xs btn-ghost" onClick={() => setReplyTo(roomId, null)}>✕</button>
        </div>
      )}
      <div className="flex gap-2 items-end">
        <input
          ref={fileInputRef}
          type="file"
          multiple
          className="hidden"
          onChange={handleFilesPicked}
          data-testid="attachment-input"
        />
        <button
          type="button"
          className="btn btn-ghost btn-sm self-end"
          aria-label="Attach file"
          title="Attach file"
          disabled={uploadMutation.isPending}
          onClick={() => fileInputRef.current?.click()}
          data-testid="attachment-button"
        >
          📎
        </button>
        <textarea
          ref={textareaRef}
          className="textarea textarea-bordered flex-1 resize-none min-h-[44px] max-h-32"
          placeholder="Type a message… (Enter to send, Shift+Enter for newline)"
          value={text}
          onChange={(e) => setText(e.target.value)}
          onKeyDown={handleKeyDown}
          onPaste={handlePaste}
          maxLength={MAX_MESSAGE_BYTES * 2}
          rows={1}
        />
        <button
          className="btn btn-primary btn-sm self-end"
          disabled={!text.trim() || overLimit || mutation.isPending}
          onClick={submit}
        >
          Send
        </button>
      </div>
      {overLimit && (
        <p className="text-xs text-error mt-1">{byteCount}/{MAX_MESSAGE_BYTES} bytes — message too long</p>
      )}
      {uploadMutation.isPending && (
        <p className="text-xs opacity-60 mt-1">Uploading…</p>
      )}
      {uploadError && (
        <p className="text-xs text-error mt-1">{uploadError}</p>
      )}
    </div>
  )
}
