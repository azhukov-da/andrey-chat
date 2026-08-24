import { expect, createRoom, joinRoom, newAccount } from './fixtures'
import { specTest } from './spec'

/**
 * One end-to-end flow claiming two scenarios.
 *
 * `chat-rooms/room-deletion/member-is-viewing-the-deleted-room` and
 * `realtime-delivery/pushed-event-kinds/room-deletion-while-viewing` describe the same moment from
 * two capabilities' points of view — the owner deletes a room, and a member who has it open is
 * navigated away while their room lists refresh. Running the flow twice to make two claims would
 * double the cost of the slowest test in the suite and assert nothing extra, so `specTest` takes
 * both identifiers (design D3).
 *
 * Nothing short of the assembled system shows this: the backend can only be observed marking a row
 * and pushing an event, and the frontend layer can only be observed reacting to an event a test
 * handed it. That one user's action moves another user's browser is visible only with two live
 * sessions against the real hub.
 */

specTest(
  [
    'chat-rooms/room-deletion/member-is-viewing-the-deleted-room',
    'realtime-delivery/pushed-event-kinds/room-deletion-while-viewing',
  ],
  'deleting a room navigates a member who has it open away from it and refreshes their room list',
  async ({ twoUsers }) => {
    const [owner, member] = twoUsers
    const roomName = `del-${newAccount('room').username}`

    const roomId = await createRoom(owner.page, roomName)
    await joinRoom(member.page, roomName)

    // Both are looking at the room. The member's presence here is the whole point: the assertions
    // below are about what happens to a browser that is *viewing* the room, not merely subscribed.
    await expect(member.page).toHaveURL(new RegExp(`/rooms/${roomId}$`))
    await expect(member.page.getByRole('heading', { name: `# ${roomName}` })).toBeVisible()

    // The member's room list must be refreshed by the event, so record that it currently has the
    // room — otherwise its later absence would prove nothing.
    //
    // Scoped by test id and matched on the link's href rather than on its text: the open room's
    // member panel is an `<aside>` too and also renders `# name`, so a looser locator picks up the
    // room being viewed as well as the room in the list.
    const sidebarRoom = member.page
      .getByTestId('right-sidebar')
      .locator(`a[href="/rooms/${roomId}"]`)
    await expect(sidebarRoom).toHaveCount(1, { timeout: 20_000 })

    owner.page.once('dialog', (dialog) => void dialog.accept())
    await owner.page.getByTestId('delete-room-button').click()
    await owner.page.waitForURL(/\/rooms$/, { timeout: 30_000 })

    // The member is moved off the deleted room without ever reloading: the push channel did it.
    await member.page.waitForURL((url) => !url.pathname.startsWith(`/rooms/${roomId}`), {
      timeout: 30_000,
    })

    // ...and their lists refreshed, so the deleted room is gone from both the sidebar and the
    // public catalogue rather than lingering until the next manual navigation.
    await expect(sidebarRoom).toHaveCount(0, { timeout: 30_000 })
    await expect(member.page.locator('.card').filter({ hasText: roomName })).toHaveCount(0, {
      timeout: 30_000,
    })
  }
)
