import { useInfiniteQuery } from '@tanstack/react-query'
import { getMessages } from '@/api/messages'

export function useMessages(roomId: string | undefined) {
  return useInfiniteQuery({
    queryKey: ['messages', roomId],
    queryFn: ({ pageParam }) => getMessages(roomId!, pageParam as string | undefined),
    enabled: !!roomId,
    initialPageParam: undefined as string | undefined,
    getNextPageParam: (last) => last.nextCursor ?? undefined,
  })
}
