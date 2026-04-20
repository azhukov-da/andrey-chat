import { useRef, useEffect, useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { sendMessage } from '@/api/messages'
import { useUiStore } from '@/stores/uiStore'
import { MAX_MESSAGE_BYTES } from '@/types'
import { getHubConnection } from '@/realtime/hubClient'
import type { Message } from '@/types'

interface Props {
  roomId: string
}

export default function MessageComposer({ roomId }: Props) {
  const textareaRef = useRef<HTMLTextAreaElement>(null)
  const queryClient = useQueryClient()
  const { getDraft, setDraft, setReplyTo } = useUiStore()
  const [text, setText] = useState(() => getDraft(roomId))
  const replyToId = useUiStore((s) => s.replyTo.get(roomId) ?? null)

  const byteCount = new TextEncoder().encode(text).length
  const overLimit = byteCount > MAX_MESSAGE_BYTES

  type Pages = { pages: { items: Message[]; nextCursor?: string | null }[] }

  const mutation = useMutation({
    mutationFn: (t: string) => sendMessage(roomId, t, replyToId ?? undefined),
    onSuccess: (msg) => {
      queryClient.setQueryData<Pages>(['messages', roomId], (old) => {
        if (!old) return old
        const [first, ...rest] = old.pages
        if (!first) return old
        return { ...old, pages: [{ ...first, items: [msg, ...first.items] }, ...rest] }
      })
      void queryClient.invalidateQueries({ queryKey: ['messages', roomId] })
    },
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

    try {
      const hub = getHubConnection()
      void hub.invoke('StopTyping', roomId)
    } catch { /* ignore */ }
  }

  const handleKeyDown = (e: React.KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault()
      submit()
    }
    if (text.length > 0) {
      try {
        const hub = getHubConnection()
        void hub.invoke('StartTyping', roomId)
      } catch { /* ignore */ }
    }
  }

  const handlePaste = (e: React.ClipboardEvent<HTMLTextAreaElement>) => {
    const files = Array.from(e.clipboardData.files)
    if (files.length > 0) {
      // attachment upload placeholder — currently just show filename
      const names = files.map((f) => f.name).join(', ')
      setText((t) => t + `[attach: ${names}]`)
      e.preventDefault()
    }
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
    </div>
  )
}
