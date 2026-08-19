import { expect } from './fixtures'
import { specTest } from './spec'
import { signIn, storedSessionId, type SignedInUser } from './fixtures'

/**
 * End-to-end tests for the two `user-sessions` revoke scenarios that only the assembled system can
 * demonstrate: what happens to the browser doing the revoking. Neither is observable at the backend
 * layer (the server just marks a row) or at the frontend layer (the outcome depends on which row the
 * server called "current").
 */

specTest(
  'user-sessions/revoking-sessions/revoking-another-session',
  'revoking a session other than the current one removes its row and leaves this browser signed in',
  async ({ signedIn, browser }) => {
    // A second browser on the same account, so there is a non-current session to revoke.
    const secondBrowser = await browser.newContext()
    const secondPage = await secondBrowser.newPage()
    await signIn(secondPage, signedIn.account)
    const secondSessionId = await storedSessionId(secondPage)
    expect(secondSessionId).not.toBeNull()

    await openSessions(signedIn)

    const rows = signedIn.page.locator('.card')
    await expect(rows).toHaveCount(2, { timeout: 30_000 })

    const currentRow = rows.filter({ hasText: 'Current' })
    const otherRow = rows.filter({ hasNotText: 'Current' })
    await expect(currentRow).toHaveCount(1)

    await otherRow.getByRole('button', { name: 'Revoke' }).click()

    // The revoked row disappears and this browser is untouched.
    await expect(rows).toHaveCount(1, { timeout: 30_000 })
    await expect(rows.first()).toContainText('Current')
    expect(signedIn.page.url()).toContain('/sessions')
    expect(await storedSessionId(signedIn.page)).not.toBeNull()

    // Still signed in: an authenticated screen keeps working.
    await signedIn.page.goto('/rooms')
    await expect(signedIn.page.getByRole('heading', { name: 'Public Rooms' })).toBeVisible()

    await secondBrowser.close()
  }
)

specTest(
  'user-sessions/revoking-sessions/revoking-the-current-session',
  'revoking the session marked Current clears credentials and returns to the sign-in screen',
  async ({ signedIn }) => {
    await openSessions(signedIn)

    const currentRow = signedIn.page.locator('.card').filter({ hasText: 'Current' })
    await expect(currentRow).toHaveCount(1, { timeout: 30_000 })

    await currentRow.getByRole('button', { name: 'Sign out' }).click()

    await signedIn.page.waitForURL(/\/login/, { timeout: 30_000 })
    await expect(signedIn.page.getByRole('button', { name: 'Sign In' })).toBeVisible()

    // Credentials are gone, not just the route.
    expect(await storedSessionId(signedIn.page)).toBeNull()
    const tokens = await signedIn.page.evaluate(() => ({
      access: sessionStorage.getItem('accessToken'),
      refresh: localStorage.getItem('refreshToken') ?? sessionStorage.getItem('refreshToken'),
    }))
    expect(tokens.access).toBeNull()
    expect(tokens.refresh).toBeNull()
  }
)

/** Navigates to the sessions screen and waits for the list to arrive. */
async function openSessions(user: SignedInUser): Promise<void> {
  await user.page.goto('/sessions')
  await expect(user.page.getByRole('heading', { name: 'Active Sessions' })).toBeVisible({
    timeout: 30_000,
  })
}
