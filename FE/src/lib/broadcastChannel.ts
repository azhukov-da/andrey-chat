const CHANNEL = 'presence-leader'
const PING_MSG = 'ping'
const PONG_MSG = 'pong'

let isLeader = false
let channel: BroadcastChannel | null = null

export function initPresenceLeader(onBecomeLeader: () => void, onLoseLeader: () => void) {
  channel = new BroadcastChannel(CHANNEL)

  channel.onmessage = (e: MessageEvent<string>) => {
    if (e.data === PING_MSG && isLeader) {
      channel?.postMessage(PONG_MSG)
    }
    if (e.data === PONG_MSG && !isLeader) {
      // another tab is already leader
    }
  }

  // Try to claim leadership by pinging; if no reply within 200ms, become leader
  channel.postMessage(PING_MSG)
  const timer = setTimeout(() => {
    isLeader = true
    onBecomeLeader()
  }, 200)

  channel.addEventListener('message', function once(e: MessageEvent<string>) {
    if (e.data === PONG_MSG) {
      clearTimeout(timer)
      channel?.removeEventListener('message', once)
    }
  })

  window.addEventListener('beforeunload', () => {
    if (isLeader) {
      isLeader = false
      onLoseLeader()
      channel?.postMessage('leader-gone')
    }
    channel?.close()
  })

  if (channel) {
    const origOnMessage = channel.onmessage
    channel.onmessage = (e: MessageEvent<string>) => {
      if (e.data === 'leader-gone' && !isLeader) {
        // try to become leader
        setTimeout(() => {
          isLeader = true
          onBecomeLeader()
        }, Math.random() * 100)
      }
      if (channel) origOnMessage?.call(channel, e)
    }
  }
}

export function isPresenceLeader() {
  return isLeader
}
