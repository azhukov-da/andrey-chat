import { vi } from 'vitest'

/**
 * A stand-in for the SignalR connection that `FE/src/realtime/hubClient.ts` hands out.
 *
 * Two things it makes possible:
 *  - **Driving server pushes.** `fakeHub.emit('MessageReceived', payload)` invokes whatever the
 *    component or `registerHubEvents` registered for that event, so the client-side reaction to
 *    any server event in `FE/src/realtime/events.ts` can be tested without a backend.
 *  - **Asserting on client invocations.** Everything the code under test invokes is recorded in
 *    `fakeHub.invocations`.
 *
 * A test file opts in by replacing the module:
 *
 * ```ts
 * vi.mock('@/realtime/hubClient', () => import('@/test/fakeHubClient'))
 * ```
 *
 * `src/test/setup.ts` resets the singleton between tests.
 */

export type HubHandler = (...args: never[]) => void

export interface HubInvocation {
  method: string
  args: unknown[]
}

export type HubConnectionState =
  | 'Disconnected'
  | 'Connecting'
  | 'Connected'
  | 'Disconnecting'
  | 'Reconnecting'

class FakeHubConnection {
  state: HubConnectionState = 'Disconnected'

  /** Every `invoke` and `send` the code under test made, oldest first. */
  readonly invocations: HubInvocation[] = []

  private readonly handlers = new Map<string, Set<HubHandler>>()
  private readonly results = new Map<string, (args: unknown[]) => unknown>()
  private readonly closeHandlers = new Set<(error?: Error) => void>()
  private readonly reconnectingHandlers = new Set<(error?: Error) => void>()
  private readonly reconnectedHandlers = new Set<(connectionId?: string) => void>()

  on(event: string, handler: HubHandler): void {
    const set = this.handlers.get(event) ?? new Set()
    set.add(handler)
    this.handlers.set(event, set)
  }

  off(event: string, handler?: HubHandler): void {
    if (!handler) {
      this.handlers.delete(event)
      return
    }
    this.handlers.get(event)?.delete(handler)
  }

  async start(): Promise<void> {
    this.state = 'Connected'
  }

  async stop(): Promise<void> {
    this.state = 'Disconnected'
    for (const handler of this.closeHandlers) handler()
  }

  async invoke<T = unknown>(method: string, ...args: unknown[]): Promise<T> {
    this.invocations.push({ method, args })
    return (this.results.get(method)?.(args) ?? undefined) as T
  }

  async send(method: string, ...args: unknown[]): Promise<void> {
    this.invocations.push({ method, args })
  }

  onclose(handler: (error?: Error) => void): void {
    this.closeHandlers.add(handler)
  }

  onreconnecting(handler: (error?: Error) => void): void {
    this.reconnectingHandlers.add(handler)
  }

  onreconnected(handler: (connectionId?: string) => void): void {
    this.reconnectedHandlers.add(handler)
  }

  // --- test-facing surface ---

  /** Fires a server-to-client event at every registered handler. */
  emit(event: string, ...args: unknown[]): void {
    const handlers = this.handlers.get(event)
    if (!handlers || handlers.size === 0) {
      throw new Error(
        `Nothing is listening for hub event '${event}'. ` +
          `Registered: ${[...this.handlers.keys()].join(', ') || '(none)'}.`
      )
    }
    for (const handler of [...handlers]) (handler as (...a: unknown[]) => void)(...args)
  }

  /** True if anything is listening for `event`. */
  listens(event: string): boolean {
    return (this.handlers.get(event)?.size ?? 0) > 0
  }

  /** Sets what `invoke(method, ...)` resolves to — e.g. the map `GetPresenceFor` returns. */
  stubResult(method: string, result: unknown | ((args: unknown[]) => unknown)): void {
    this.results.set(method, typeof result === 'function' ? (result as (a: unknown[]) => unknown) : () => result)
  }

  /** Invocations of one method, in order. */
  invocationsOf(method: string): HubInvocation[] {
    return this.invocations.filter((i) => i.method === method)
  }

  /** Simulates the reconnect callbacks `withAutomaticReconnect` would fire. */
  simulateReconnect(): void {
    this.state = 'Reconnecting'
    for (const handler of this.reconnectingHandlers) handler()
    this.state = 'Connected'
    for (const handler of this.reconnectedHandlers) handler('reconnected-id')
  }

  reset(): void {
    this.state = 'Disconnected'
    this.invocations.length = 0
    this.handlers.clear()
    this.results.clear()
    this.closeHandlers.clear()
    this.reconnectingHandlers.clear()
    this.reconnectedHandlers.clear()
  }
}

/** The single fake connection every test shares, reset between tests. */
export const fakeHub = new FakeHubConnection()

/** Records calls to `startHub`/`stopHub` so a test can assert the lifecycle was driven. */
export const hubLifecycle = {
  start: vi.fn(async () => {
    await fakeHub.start()
  }),
  stop: vi.fn(async () => {
    await fakeHub.stop()
  }),
}

export function resetFakeHub(): void {
  fakeHub.reset()
  hubLifecycle.start.mockClear()
  hubLifecycle.stop.mockClear()
}
