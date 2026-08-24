import {
  test as base,
  expect,
  type Browser,
  type BrowserContext,
  type Page,
} from '@playwright/test'

/**
 * Fixtures for the end-to-end layer.
 *
 * Users are created fresh per test with generated identifiers, so tests never collide and never
 * depend on data another run left behind. Nothing is cleaned up afterwards: this layer runs against
 * a developer's own stack, which is explicitly outside the isolation guarantee that covers the
 * backend integration layer (see design D8). `docker compose down -v` resets it when the
 * accumulated test accounts start to bother anyone.
 */

export const TEST_PASSWORD = 'Passw0rd!'

export interface TestAccount {
  email: string
  username: string
  password: string
}

/** A browser session belonging to one account. */
export interface SignedInUser {
  account: TestAccount
  context: BrowserContext
  page: Page
}

export function newAccount(label = 'e2e'): TestAccount {
  const suffix = `${Date.now().toString(36)}${Math.random().toString(36).slice(2, 8)}`
  return {
    email: `${label}-${suffix}@example.test`,
    username: `${label}${suffix}`.replace(/[^a-z0-9]/gi, '').slice(0, 20),
    password: TEST_PASSWORD,
  }
}

interface Fixtures {
  /** Registers a new account and returns a browser context already signed in as it. */
  signedIn: SignedInUser
  /** Two independent browser contexts on two different accounts, for cross-user behaviour. */
  twoUsers: [SignedInUser, SignedInUser]
  /** Registers an account and signs it in on a context the test creates itself. */
  createUser: (label?: string) => Promise<SignedInUser>
}

export const test = base.extend<Fixtures>({
  createUser: async ({ browser }, use) => {
    const opened: BrowserContext[] = []

    await use(async (label = 'e2e') => {
      const context = await browser.newContext()
      opened.push(context)
      const page = await context.newPage()
      const account = newAccount(label)
      // Registration signs the new account straight in, so there is no separate sign-in step.
      await registerAccount(page, account)
      return { account, context, page }
    })

    for (const context of opened) await context.close()
  },

  signedIn: async ({ createUser }, use) => {
    await use(await createUser('user'))
  },

  twoUsers: async ({ createUser }, use) => {
    await use([await createUser('alice'), await createUser('bob')])
  },
})

export { expect }

/**
 * Navigates to the sign-in screen the only way the deployed stack allows.
 *
 * `nginx.conf` proxies `/login` and `/register` to the backend, because ASP.NET Identity owns
 * those paths — so `page.goto('/login')` gets HTTP 405 from the API instead of the SPA, and a
 * refresh on either screen does the same. The app is only reachable through `/`, which boots the
 * SPA and then routes to `/login` client-side. That collision is a real defect in the deployed
 * topology and not something this harness introduced; the tests route around it rather than
 * pretending those URLs work.
 */
async function openSignInScreen(page: Page): Promise<void> {
  await page.goto('/')
  await page.waitForURL(/\/login/, { timeout: 30_000 })
  await page.getByRole('button', { name: 'Sign In' }).waitFor({ timeout: 20_000 })
}

/**
 * Registers through the real registration screen, reached by the "Register" link on the sign-in
 * screen so navigation stays client-side. The screen signs the new account in on success and lands
 * on `/rooms`, which is where this returns from — so the browser also has a registered session.
 *
 * The auth forms use DaisyUI's `<label class="label"><span class="label-text">` markup, which is
 * not associated with its input by `for`/`id` — so `getByLabel` finds nothing and the fields have
 * to be located by input type and order. Worth fixing in the components; until then this is what
 * the DOM actually offers.
 */
export async function registerAccount(page: Page, account: TestAccount): Promise<void> {
  await openSignInScreen(page)
  await page.getByRole('link', { name: 'Register' }).click()

  const form = page.locator('form')
  await form.locator('input[type="email"]').waitFor({ timeout: 20_000 })
  await form.locator('input[type="email"]').fill(account.email)
  await form.locator('input[type="text"]').first().fill(account.username)

  const passwords = form.locator('input[type="password"]')
  await passwords.nth(0).fill(account.password)
  await passwords.nth(1).fill(account.password)

  await page.getByRole('button', { name: /register|sign up|create/i }).click()
  await page.waitForURL(/\/rooms/, { timeout: 30_000 })
}

export interface SignInOptions {
  /**
   * Ticks the sign-in screen's "Keep me signed in" checkbox. Left off by default so the existing
   * call sites keep the behaviour they were written against. What the choice actually decides is
   * *where the refresh token is stored*: `localStorage` when set, `sessionStorage` when not
   * (`FE/src/stores/authStore.ts`) — which is what makes it survive, or not survive, closing the
   * browser. See `reopenBrowser`.
   */
  keepSignedIn?: boolean
}

/**
 * Signs an already-registered account in through the real sign-in screen, which is also what
 * registers the browser's session — the behaviour under test in several `user-sessions` scenarios.
 * Expects the browser to be signed out.
 */
export async function signIn(
  page: Page,
  account: TestAccount,
  options: SignInOptions = {}
): Promise<void> {
  await openSignInScreen(page)
  const form = page.locator('form')
  await form.locator('input[type="email"]').fill(account.email)
  await form.locator('input[type="password"]').fill(account.password)
  if (options.keepSignedIn) await form.locator('input[type="checkbox"]').check()
  await page.getByRole('button', { name: 'Sign In' }).click()
  await page.waitForURL((url) => !url.pathname.startsWith('/login'), { timeout: 20_000 })
}

/**
 * Closes this browser session and opens a new one carrying the same persistent storage — the
 * closest a test can get to the user quitting the browser and launching it again.
 *
 * Playwright's `storageState()` captures cookies and `localStorage` but deliberately not
 * `sessionStorage`, which is exactly the line the `authentication/persistent-login` requirement
 * draws: "keep me signed in" puts the refresh token in `localStorage` and its absence puts it in
 * `sessionStorage`. So carrying the state across is not an approximation of a browser restart —
 * the two storages behave here precisely as they would in a real one.
 *
 * Note the access token lives in `sessionStorage` in *both* cases, so the reopened browser never
 * has one. A restored session therefore depends on the refresh token being exchanged at `/refresh`
 * during bootstrap, which is why callers must wait on a rendered screen rather than on a token.
 *
 * The old context is closed; the caller owns closing the returned one.
 */
export async function reopenBrowser(
  user: SignedInUser,
  browser: Browser
): Promise<{ context: BrowserContext; page: Page }> {
  const storageState = await user.context.storageState()
  await user.context.close()

  const context = await browser.newContext({ storageState })
  const page = await context.newPage()
  return { context, page }
}

/**
 * Creates a room through the real UI and returns its id, taken from the URL the app lands on.
 *
 * Deliberately not seeded over HTTP: a test that set its fixtures up by calling the API directly
 * would skip the very proxy path that `platform-constraints` asks these tests to exercise, and
 * would stop noticing if creating a room from the browser broke.
 */
export async function createRoom(page: Page, name: string): Promise<string> {
  await page.goto('/rooms')
  await page.getByRole('button', { name: '+ New Room' }).click()

  const dialog = page.locator('dialog.modal-open')
  await dialog.locator('input.input-bordered').first().fill(name)
  await dialog.getByRole('button', { name: 'Create' }).click()

  await page.waitForURL(/\/rooms\/[0-9a-f-]{36}/, { timeout: 30_000 })
  const id = page.url().split('/rooms/')[1]
  if (!id) throw new Error(`Creating room "${name}" did not land on a room URL: ${page.url()}`)
  return id
}

/**
 * Joins a public room by name from the catalogue and opens it, returning its id.
 *
 * The catalogue reloads between joining and opening because `PublicCatalog` invalidates only
 * `['rooms','mine']` on a successful join, so the catalogue's own row keeps offering "Join" until
 * its query is refetched. That is the app's behaviour, not something the test can assert away —
 * the reload is how a user would get the same result.
 */
export async function joinRoom(page: Page, name: string): Promise<string> {
  await page.goto('/rooms')
  const row = page.locator('.card').filter({ hasText: name })
  await row.first().waitFor({ timeout: 30_000 })

  const join = row.getByRole('button', { name: 'Join' })
  if (await join.count()) {
    await join.first().click()
    await page.waitForTimeout(500)
    await page.reload()
  }

  await page
    .locator('.card')
    .filter({ hasText: name })
    .getByRole('button', { name: 'Open' })
    .first()
    .click()

  await page.waitForURL(/\/rooms\/[0-9a-f-]{36}/, { timeout: 30_000 })
  const id = page.url().split('/rooms/')[1]
  if (!id) throw new Error(`Joining room "${name}" did not land on a room URL: ${page.url()}`)
  return id
}

/** Every URL a page reached out to, collected by `recordRequests`. */
export interface RecordedTraffic {
  /** URLs of every HTTP request the page issued, in order, including the document itself. */
  http: string[]
  /** URLs of every WebSocket the page opened. */
  webSocket: string[]
}

/**
 * Starts recording the URLs a page contacts, for the transport-boundary requirement.
 *
 * Both listeners stay attached for the life of the page; the returned object is mutated in place,
 * so a caller reads it after driving whatever session it wants to observe.
 */
export function recordRequests(page: Page): RecordedTraffic {
  const traffic: RecordedTraffic = { http: [], webSocket: [] }
  page.on('request', (request) => traffic.http.push(request.url()))
  page.on('websocket', (ws) => traffic.webSocket.push(ws.url()))
  return traffic
}

/** The session identifier this browser stored, or null if it has none. */
export function storedSessionId(page: Page): Promise<string | null> {
  return page.evaluate(() => localStorage.getItem('sessionId'))
}
