import { useInfiniteQuery } from '@tanstack/react-query'
import { getPublicRooms } from '@/api/rooms'

export function usePublicRooms(search: string) {
  return useInfiniteQuery({
    queryKey: ['rooms', 'public', search],
    queryFn: ({ pageParam }) => getPublicRooms(search || undefined, pageParam as number),
    initialPageParam: 1,
    getNextPageParam: (last) => {
      const totalPages = Math.ceil(last.totalCount / last.pageSize)
      return last.page < totalPages ? last.page + 1 : undefined
    },
  })
}
