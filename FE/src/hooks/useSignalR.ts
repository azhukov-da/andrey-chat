import { useEffect } from 'react'
import { useNavigate } from 'react-router'
import { useQueryClient } from '@tanstack/react-query'
import { useAuthStore } from '@/stores/authStore'
import { startHub, stopHub } from '@/realtime/hubClient'
import { registerHubEvents } from '@/realtime/events'
import { startPresencePing, stopPresencePing } from '@/realtime/presencePing'
import { startAfkDetector, stopAfkDetector } from '@/lib/afkDetector'
import { initPresenceLeader } from '@/lib/broadcastChannel'
import { getHubConnection } from '@/realtime/hubClient'

export function useSignalR() {
  const me = useAuthStore((s) => s.me)
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  useEffect(() => {
    if (!me) return

    const hub = getHubConnection()
    const unregisterEvents = registerHubEvents(hub, queryClient, navigate)

    startAfkDetector()
    initPresenceLeader(
      () => startPresencePing(),
      () => stopPresencePing()
    )

    void startHub().then(() => startPresencePing())

    const handleLogout = () => { void stopHub() }
    window.addEventListener('auth/logout', handleLogout)

    return () => {
      unregisterEvents()
      stopPresencePing()
      stopAfkDetector()
      void stopHub()
      window.removeEventListener('auth/logout', handleLogout)
    }
  // queryClient and navigate are stable refs — excluded intentionally to prevent
  // hub teardown/restart on every navigation
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [me])
}
