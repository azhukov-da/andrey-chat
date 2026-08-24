import { expect, recordRequests, test, type SignedInUser } from './fixtures'
import { specTest } from './spec'

/**
 * End-to-end test for `realtime-delivery/automatic-reconnection/presence-after-reconnect`.
 *
 * The scenario is about what the client does after a real network interruption, so it needs a real
 * one: the frontend layer can only show the handler being registered, and the backend layer never
 * sees the client's reconnect logic at all.
 *
 * Slowest test in the suite by design. `withAutomaticReconnect([0, 1000, 2000, 5000, 10000])` means
 * a reconnect can legitimately take twenty seconds before presence is even re-requested, so this
 * file raises its own timeout rather than borrowing the suite default.
 */
test.describe.configure({ timeout: 120_000 })

specTest(
  'realtime-delivery/automatic-reconnection/presence-after-reconnect',
  'presence for the displayed users is requested again after a dropped connection is re-established',
  async ({ twoUsers }) => {
    const [alice, bob] = twoUsers

    // A direct chat is what puts another user's presence on Alice's screen — `RightSidebar` is the
    // view that both displays presence and re-requests it on reconnect, and it tracks direct-chat
    // partners. Building it through the real friend flow, because that is the only route the UI
    // offers to a direct chat.
    await befriend(alice, bob)
    await openDirectChat(alice, bob)

    const traffic = recordRequests(alice.page)
    await alice.page.goto('/rooms')

    // Bob is connected, so Alice's indicator for him reads online. This is the state that must go
    // stale while she is offline — without it, the assertion at the end would pass on a client that
    // never learned anything, since `usePresence` defaults to offline for unknown users.
    const bobDot = presenceDotFor(alice, bob)
    await expect(bobDot).toHaveAttribute('title', 'online', { timeout: 60_000 })

    const negotiationsBefore = negotiations(traffic)

    // A real interruption: the transport fails rather than closing politely.
    await alice.context.setOffline(true)

    // Wait until the client has actually noticed and started retrying, so what follows is a genuine
    // reconnect rather than a connection that never dropped.
    await expect
      .poll(() => negotiations(traffic), { timeout: 30_000, intervals: [250] })
      .toBeGreaterThan(negotiationsBefore)

    // Bob leaves while Alice cannot hear about it, so the indicator she is holding is now wrong and
    // no pushed event can correct it. Only a re-request on reconnect can.
    await bob.context.close()

    // Back online promptly. The scenario says the network "returns shortly afterwards", and that
    // matters: `withAutomaticReconnect([0, 1000, 2000, 5000, 10000])` gives up for good once its
    // five delays are spent, roughly eighteen seconds in. An outage longer than that is a different
    // scenario — one the specification does not claim the client survives.
    await alice.context.setOffline(false)

    // The stale indicator is corrected, without any user action: the client reconnected on its own
    // and asked for presence again. A client that reconnected but did not re-request would still be
    // showing Bob online here.
    await expect(bobDot).toHaveAttribute('title', 'offline', { timeout: 60_000 })
  }
)

/**
 * How many times the client has negotiated a hub connection — the transport-independent signal that
 * it is trying to (re)connect.
 *
 * Counting negotiations rather than websockets on purpose: through the deployed nginx the WebSocket
 * upgrade does not complete and SignalR falls back to long polling, so a reconnect over the real
 * stack opens no new socket at all. Nothing in the specification requires the websocket transport
 * specifically — it requires one connection that comes back — so this counts the thing that
 * actually happens.
 */
function negotiations(traffic: { http: string[] }): number {
  return traffic.http.filter((url) => url.includes('/hubs/chat/negotiate')).length
}

/** The presence indicator Alice's sidebar shows for her direct-chat partner. */
function presenceDotFor(viewer: SignedInUser, partner: SignedInUser) {
  return viewer.page
    .locator('aside a')
    .filter({ hasText: partner.account.username })
    .locator('span.badge')
    .first()
}

/** Sends a contact request from one user and accepts it as the other, through the real screens. */
async function befriend(from: SignedInUser, to: SignedInUser): Promise<void> {
  await from.page.goto('/contacts')
  await from.page.getByRole('button', { name: '+ Add Contact' }).click()
  await from.page.getByPlaceholder('Enter username or email').fill(to.account.username)
  await from.page.getByRole('button', { name: 'Send Request' }).click()

  await to.page.goto('/contacts')
  const request = to.page.locator('div').filter({ hasText: from.account.username })
  await request.getByRole('button', { name: 'Accept' }).first().click({ timeout: 30_000 })

  await expect(to.page.getByRole('button', { name: 'Message' }).first()).toBeVisible({
    timeout: 30_000,
  })
}

/** Opens the direct chat with an accepted contact, which is what registers them for presence. */
async function openDirectChat(viewer: SignedInUser, partner: SignedInUser): Promise<void> {
  await viewer.page.goto('/contacts')
  const row = viewer.page.locator('div').filter({ hasText: `@${partner.account.username}` })
  await row.getByRole('button', { name: 'Message' }).first().click({ timeout: 30_000 })
  await viewer.page.waitForURL(/\/rooms\/[0-9a-f-]{36}/, { timeout: 30_000 })
}
