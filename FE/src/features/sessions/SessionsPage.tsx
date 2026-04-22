import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { listSessions, revokeSession, type UserSessionDto } from '@/api/sessions'
import { useAuth } from '@/hooks/useAuth'

export default function SessionsPage() {
  const queryClient = useQueryClient()
  const { logout } = useAuth()

  const { data, isLoading, error } = useQuery({
    queryKey: ['sessions'],
    queryFn: listSessions,
  })

  const revokeMutation = useMutation({
    mutationFn: (session: UserSessionDto) => revokeSession(session.id).then(() => session),
    onSuccess: (session) => {
      if (session.isCurrent) {
        logout()
      } else {
        void queryClient.invalidateQueries({ queryKey: ['sessions'] })
      }
    },
  })

  return (
    <div className="p-6 max-w-3xl mx-auto">
      <h1 className="text-2xl font-bold mb-6">Active Sessions</h1>

      {isLoading && <span className="loading loading-spinner" />}
      {error && <div className="alert alert-error">Failed to load sessions</div>}

      <div className="space-y-3">
        {data?.map((s) => (
          <div key={s.id} className="card bg-base-100 shadow">
            <div className="card-body">
              <div className="flex items-start justify-between gap-3">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <h2 className="card-title text-base">
                      {s.deviceInfo || 'Unknown device'}
                    </h2>
                    {s.isCurrent && <span className="badge badge-primary badge-sm">Current</span>}
                  </div>
                  <p className="text-sm text-base-content/60 break-all">
                    {s.userAgent || 'no user agent'}
                  </p>
                  <p className="text-xs text-base-content/50 mt-1">
                    IP: {s.ipAddress || 'unknown'} &middot; Created: {new Date(s.createdAt).toLocaleString()} &middot; Last seen: {new Date(s.lastSeenAt).toLocaleString()}
                  </p>
                </div>
                <button
                  className="btn btn-sm btn-error btn-outline"
                  onClick={() => revokeMutation.mutate(s)}
                  disabled={revokeMutation.isPending}
                >
                  {s.isCurrent ? 'Sign out' : 'Revoke'}
                </button>
              </div>
            </div>
          </div>
        ))}
        {data && data.length === 0 && (
          <p className="text-sm text-base-content/60">No active sessions.</p>
        )}
      </div>
    </div>
  )
}
