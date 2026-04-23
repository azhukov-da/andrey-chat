import { useEffect, useRef } from 'react'
import { Virtuoso, type VirtuosoHandle } from 'react-virtuoso'
import { useMessages } from '@/hooks/useMessages'
import { useUnread } from '@/hooks/useUnread'
import { useAuthStore } from '@/stores/authStore'
import MessageItem from './MessageItem'
import type { Message } from '@/types'

interface Props {
  roomId: string
}

export default function MessageList({ roomId }: Props) {
  const { data, fetchNextPage, hasNextPage, isFetchingNextPage } = useMessages(roomId)
  const { markRead } = useUnread(roomId)
  const me = useAuthStore((s) => s.me)
  const virtuosoRef = useRef<VirtuosoHandle>(null)
  const lastMessageId = useRef<string | null>(null)

  const messages: Message[] = (data?.pages ?? []).flatMap((p) => p.items).reverse()

  useEffect(() => {
    const last = messages[messages.length - 1]
    if (last && last.id !== lastMessageId.current) {
      lastMessageId.current = last.id
      void markRead(last.id)
    }
  }, [messages, markRead])

  if (!data) {
    return (
      <div className="flex-1 flex items-center justify-center">
        <span className="loading loading-spinner" />
      </div>
    )
  }

  return (
    <div className="flex-1 overflow-hidden">
      <Virtuoso
        ref={virtuosoRef}
        data={messages}
        followOutput="smooth"
        initialTopMostItemIndex={messages.length - 1}
        startReached={() => {
          if (hasNextPage && !isFetchingNextPage) void fetchNextPage()
        }}
        components={{
          Header: () =>
            isFetchingNextPage ? (
              <div className="flex justify-center py-2">
                <span className="loading loading-spinner loading-xs" />
              </div>
            ) : null,
        }}
        itemContent={(_, msg) => {
          const replyTo = msg.replyToMessageId
            ? messages.find((m) => m.id === msg.replyToMessageId)
            : undefined
          const props = {
            message: msg,
            isMine: msg.authorId === me?.id,
            roomId,
            ...(replyTo ? { replyTo } : {}),
          }
          return <MessageItem key={msg.id} {...props} />
        }}
      />
    </div>
  )
}
