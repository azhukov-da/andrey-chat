import { test as base, expect, type BrowserContext, type Page } from '@playwright/test'

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

/**
 * Signs an already-registered account in through the real sign-in screen, which is also what
 * registers the browser's session — the behaviour under test in several `user-sessions` scenarios.
 * Expects the browser to be signed out.
 */
export async function signIn(page: Page, account: TestAccount): Promise<void> {
  await openSignInScreen(page)
  const form = page.locator('form')
  await form.locator('input[type="email"]').fill(account.email)
  await form.locator('input[type="password"]').fill(account.password)
  await page.getByRole('button', { name: 'Sign In' }).click()
  await page.waitForURL((url) => !url.pathname.startsWith('/login'), { timeout: 20_000 })
}

/** The session identifier this browser stored, or null if it has none. */
export function storedSessionId(page: Page): Promise<string | null> {
  return page.evaluate(() => localStorage.getItem('sessionId'))
}
