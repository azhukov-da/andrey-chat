import { getHubConnection } from './hubClient'
import { isUserActive } from '@/lib/afkDetector'
import { isPresenceLeader } from '@/lib/broadcastChannel'

const PING_INTERVAL_MS = 20_000

let intervalId: ReturnType<typeof setInterval> | null = null

function ping() {
  if (!isPresenceLeader()) return
  const hub = getHubConnection()
  const active = document.visibilityState === 'visible' && isUserActive()
  hub.invoke('Ping', active).catch(() => { /* ignore ping errors */ })
}

export function startPresencePing() {
  if (intervalId) return
  intervalId = setInterval(ping, PING_INTERVAL_MS)

  document.addEventListener('visibilitychange', ping)
  window.addEventListener('online', ping)
}

export function stopPresencePing() {
  if (intervalId) {
    clearInterval(intervalId)
    intervalId = null
  }
  document.removeEventListener('visibilitychange', ping)
  window.removeEventListener('online', ping)
}
