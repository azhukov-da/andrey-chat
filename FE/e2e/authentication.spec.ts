import { expect, reopenBrowser, signIn } from './fixtures'
import { specTest } from './spec'

/**
 * End-to-end tests for the two `authentication/persistent-login` scenarios.
 *
 * Both are here for the same reason: whether credentials survive closing the browser is a property
 * of the browser, not of the code. The frontend layer can assert which storage the token was
 * written to, but it cannot assert that `sessionStorage` is actually gone the next time the
 * application opens — only a real second browser session shows that. See design D2.
 *
 * The fixture signs each account in, so these tests sign *out* first and then sign in again through
 * the real screen, because the checkbox under test only exists there.
 */

/**
 * Skipped: the application has no startup path that exchanges a persisted refresh token, so a
 * reopened browser always lands on the sign-in screen even though the token is sitting in
 * `localStorage` exactly as "keep me signed in" promises. `useAuth.bootstrapAuth` is the function
 * that would do it and nothing in the application calls it; `AppShell` reads only the access token,
 * which lives in `sessionStorage` and is therefore always absent after a restart. The assertions
 * below are the ones the specification asks for and are left as they are — see the
 * `persistent-login/keep-me-signed-in` row in `docs/spec-gaps.md`.
 */
specTest.skip(
  'authentication/persistent-login/keep-me-signed-in',
  'signing in with "keep me signed in" restores the session after the browser is closed and reopened',
  async ({ signedIn, browser }) => {
    await signOut(signedIn.page)
    await signIn(signedIn.page, signedIn.account, { keepSignedIn: true })

    // The choice is only meaningful if it actually put the refresh token in persistent storage —
    // that is the thing a closed browser keeps.
    expect(await refreshTokenLocations(signedIn.page)).toEqual({ local: true, session: false })

    const reopened = await reopenBrowser(signedIn, browser)
    await reopened.page.goto('/')

    // Signed in again with no credentials re-entered: the app lands on an authenticated screen
    // rather than routing to /login. The wait is on the screen, not on a token, because the access
    // token lived in sessionStorage and has to be re-obtained at /refresh first (design D2).
    await expect(reopened.page.getByRole('heading', { name: 'Public Rooms' })).toBeVisible({
      timeout: 30_000,
    })
    expect(reopened.page.url()).not.toContain('/login')

    await reopened.context.close()
  }
)

specTest(
  'authentication/persistent-login/not-persisted',
  'signing in without "keep me signed in" requires signing in again once the browser session ends',
  async ({ signedIn, browser }) => {
    await signOut(signedIn.page)
    await signIn(signedIn.page, signedIn.account, { keepSignedIn: false })

    // The mirror image of the other test: the refresh token is in sessionStorage, which is exactly
    // what does not outlive the browser session.
    expect(await refreshTokenLocations(signedIn.page)).toEqual({ local: false, session: true })

    const reopened = await reopenBrowser(signedIn, browser)
    await reopened.page.goto('/')

    await reopened.page.waitForURL(/\/login/, { timeout: 30_000 })
    await expect(reopened.page.getByRole('button', { name: 'Sign In' })).toBeVisible()

    await reopened.context.close()
  }
)

/**
 * Signs out through the user menu so the browser is in the state the sign-in screen expects.
 *
 * Uses the menu rather than clearing storage by script: clearing storage would leave the server's
 * session row behind, and the next sign-in would then be the account's second session rather than
 * its first — a difference these tests do not care about but which would quietly muddy any failure.
 */
async function signOut(page: import('@playwright/test').Page): Promise<void> {
  await page.goto('/rooms')
  await page.locator('.dropdown label[tabindex="0"]').click()
  await page.getByRole('button', { name: 'Sign Out' }).click()
  await page.waitForURL(/\/login/, { timeout: 30_000 })
}

/** Where the refresh token ended up — the storage decision "keep me signed in" actually makes. */
function refreshTokenLocations(
  page: import('@playwright/test').Page
): Promise<{ local: boolean; session: boolean }> {
  return page.evaluate(() => ({
    local: localStorage.getItem('refreshToken') !== null,
    session: sessionStorage.getItem('refreshToken') !== null,
  }))
}
