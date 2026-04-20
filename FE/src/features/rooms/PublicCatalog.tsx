import { useState } from 'react'
import { useNavigate } from 'react-router'
import { useInfiniteQuery, useMutation, useQueryClient } from '@tanstack/react-query'
import { getPublicRooms, joinRoom } from '@/api/rooms'
import CreateRoomDialog from './CreateRoomDialog'

export default function PublicCatalog() {
  const [search, setSearch] = useState('')
  const [debouncedSearch, setDebouncedSearch] = useState('')
  const [showCreate, setShowCreate] = useState(false)
  const navigate = useNavigate()
  const queryClient = useQueryClient()

  const { data, fetchNextPage, hasNextPage, isFetchingNextPage, isLoading } = useInfiniteQuery({
    queryKey: ['rooms', 'public', debouncedSearch],
    queryFn: ({ pageParam }) => getPublicRooms(debouncedSearch || undefined, pageParam as number),
    initialPageParam: 1,
    getNextPageParam: (last) => {
      const totalPages = Math.ceil(last.totalCount / last.pageSize)
      return last.page < totalPages ? last.page + 1 : undefined
    },
  })

  const joinMutation = useMutation({
    mutationFn: (id: string) => joinRoom(id),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ['rooms', 'mine'] })
    },
  })

  const rooms = data?.pages.flatMap((p) => p.items) ?? []

  const handleSearchChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const v = e.target.value
    setSearch(v)
    clearTimeout(window._searchTimer)
    window._searchTimer = window.setTimeout(() => setDebouncedSearch(v), 250)
  }

  return (
    <div className="p-6 max-w-3xl mx-auto">
      <div className="flex items-center justify-between mb-4">
        <h1 className="text-2xl font-bold">Public Rooms</h1>
        <button className="btn btn-primary btn-sm" onClick={() => setShowCreate(true)}>+ New Room</button>
      </div>
      <input
        type="text"
        className="input input-bordered w-full mb-4"
        placeholder="Search rooms…"
        value={search}
        onChange={handleSearchChange}
      />

      {isLoading && <div className="flex justify-center py-8"><span className="loading loading-spinner" /></div>}

      <div className="space-y-3">
        {rooms.map((room) => (
          <div key={room.id} className="card card-compact bg-base-100 shadow">
            <div className="card-body flex-row items-center justify-between">
              <div>
                <h3 className="font-semibold">
                  # {room.name}
                  <span className="badge badge-outline badge-sm ml-2">{room.memberCount} members</span>
                </h3>
                {room.description && <p className="text-sm text-base-content/70">{room.description}</p>}
              </div>
              {room.myRole != null ? (
                <button className="btn btn-sm btn-outline" onClick={() => navigate(`/rooms/${room.id}`)}>
                  Open
                </button>
              ) : (
                <button
                  className="btn btn-sm btn-primary"
                  disabled={joinMutation.isPending}
                  onClick={() => joinMutation.mutate(room.id)}
                >
                  Join
                </button>
              )}
            </div>
          </div>
        ))}
      </div>

      {hasNextPage && (
        <div className="flex justify-center mt-4">
          <button className="btn btn-outline btn-sm" onClick={() => fetchNextPage()} disabled={isFetchingNextPage}>
            {isFetchingNextPage ? <span className="loading loading-spinner loading-xs" /> : 'Load more'}
          </button>
        </div>
      )}

      {showCreate && <CreateRoomDialog onClose={() => setShowCreate(false)} />}
    </div>
  )
}

declare global {
  interface Window { _searchTimer: number | undefined }
}
