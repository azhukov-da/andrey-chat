import type { HubConnection } from '@microsoft/signalr'
import { fakeHub, hubLifecycle } from './fakeHub'

/**
 * Module replacement for `@/realtime/hubClient`. A test file installs it with:
 *
 * ```ts
 * vi.mock('@/realtime/hubClient', () => import('@/test/fakeHubClient'))
 * ```
 *
 * The exports match `hubClient.ts` one for one, so anything importing it — `useSignalR`,
 * `registerHubEvents`, `presencePing` — runs unchanged against `fakeHub`.
 */

export function getHubConnection(): HubConnection {
  return fakeHub as unknown as HubConnection
}

export function startHub(): Promise<void> {
  return hubLifecycle.start()
}

export function stopHub(): Promise<void> {
  return hubLifecycle.stop()
}
