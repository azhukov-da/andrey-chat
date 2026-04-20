import { useQuery } from '@tanstack/react-query'
import { getRoom } from '@/api/rooms'

export function useRoom(id: string | undefined) {
  return useQuery({
    queryKey: ['rooms', id],
    queryFn: () => getRoom(id!),
    enabled: !!id,
  })
}
