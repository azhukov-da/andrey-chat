import { expect, signIn, storedSessionId } from './fixtures'
import { specTest } from './spec'

/**
 * End-to-end test for `chat-ui-shell/top-navigation/signing-out-from-the-menu`.
 *
 * The requirement has two halves — "the current session is revoked" and "the sign-in screen is
 * shown" — and they are enforced in different places. The frontend layer can show that the menu
 * item calls something; only the assembled system shows that what it called actually removed the
 * session row the server keeps. So the test asserts both, and asserts the revocation from a
 * *second* browser on the same account, which is the only vantage point from which the first
 * browser's session can still be observed after it has signed itself out (design D7).
 */

specTest(
  'chat-ui-shell/top-navigation/signing-out-from-the-menu',
  'choosing sign out in the user menu revokes the session and returns to the sign-in screen',
  async ({ signedIn, browser }) => {
    // A second browser on the same account: the observer that outlives the sign-out.
    const observerContext = await browser.newContext()
    const observerPage = await observerContext.newPage()
    await signIn(observerPage, signedIn.account)

    const sessionId = await storedSessionId(signedIn.page)
    expect(sessionId).not.toBeNull()

    // The observer can see both sessions before anything is signed out.
    await openSessions(observerPage)
    await expect(observerPage.locator('.card')).toHaveCount(2, { timeout: 30_000 })

    // Sign out through the user menu, which is what the requirement names.
    await signedIn.page.goto('/rooms')
    await signedIn.page.locator('.dropdown label[tabindex="0"]').click()
    await signedIn.page.getByRole('button', { name: 'Sign Out' }).click()

    // Half one: the sign-in screen is shown, and the credentials are actually gone rather than the
    // route merely having changed.
    await signedIn.page.waitForURL(/\/login/, { timeout: 30_000 })
    await expect(signedIn.page.getByRole('button', { name: 'Sign In' })).toBeVisible()
    expect(await storedSessionId(signedIn.page)).toBeNull()
    const tokens = await signedIn.page.evaluate(() => ({
      access: sessionStorage.getItem('accessToken'),
      refresh: localStorage.getItem('refreshToken') ?? sessionStorage.getItem('refreshToken'),
    }))
    expect(tokens.access).toBeNull()
    expect(tokens.refresh).toBeNull()

    // Half two: the session was revoked server-side, not just forgotten locally. The observer
    // reloads its own sessions list and finds only itself left.
    await observerPage.reload()
    await expect(observerPage.locator('.card')).toHaveCount(1, { timeout: 30_000 })
    await expect(observerPage.locator('.card').first()).toContainText('Current')

    await observerContext.close()
  }
)

/** Navigates to the sessions screen and waits for the list to arrive. */
async function openSessions(page: import('@playwright/test').Page): Promise<void> {
  await page.goto('/sessions')
  await expect(page.getByRole('heading', { name: 'Active Sessions' })).toBeVisible({
    timeout: 30_000,
  })
}
