const IDLE_THRESHOLD_MS = 60_000

let lastActivity = Date.now()

const events = ['mousemove', 'keydown', 'touchstart', 'wheel'] as const

let throttleTimer: ReturnType<typeof setTimeout> | null = null

function onActivity() {
  if (throttleTimer) return
  throttleTimer = setTimeout(() => {
    lastActivity = Date.now()
    throttleTimer = null
  }, 1000)
}

export function startAfkDetector() {
  for (const e of events) window.addEventListener(e, onActivity, { passive: true })
}

export function stopAfkDetector() {
  for (const e of events) window.removeEventListener(e, onActivity)
}

export function isUserActive(): boolean {
  return Date.now() - lastActivity < IDLE_THRESHOLD_MS
}
