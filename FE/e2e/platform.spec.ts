import { execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { expect, recordRequests } from './fixtures'
import { specTest } from './spec'

const run = promisify(execFile)

/**
 * End-to-end tests for the two `platform-constraints` scenarios that describe the shape of the
 * running system rather than any single feature.
 *
 * Both are here because they are claims about the deployment, and a deployment is not something the
 * backend or frontend layer can see: one asks which origins the browser actually contacts, which is
 * observable only from inside a browser, and the other asks which services the compose stack
 * publishes, which is observable only from the host running it.
 */

specTest(
  'platform-constraints/client-server-transport-boundary/proxied-call',
  'the browser reaches the backend only through relative paths on its own origin',
  async ({ signedIn, baseURL }) => {
    const origin = new URL(baseURL ?? 'http://localhost:3000').host

    // Recording starts before the session is driven, so nothing the page does escapes the census.
    const traffic = recordRequests(signedIn.page)

    // A representative session touching every channel the requirement names: REST, identity, the
    // real-time hub, and attachment content.
    await signedIn.page.goto('/rooms')
    await signedIn.page.getByRole('button', { name: '+ New Room' }).click()
    const dialog = signedIn.page.locator('dialog.modal-open')
    await dialog.locator('input.input-bordered').first().fill(`transport-${Date.now()}`)
    await dialog.getByRole('button', { name: 'Create' }).click()
    await signedIn.page.waitForURL(/\/rooms\/[0-9a-f-]{36}/, { timeout: 30_000 })

    await signedIn.page.getByPlaceholder('Type a message').fill('transport boundary')
    await signedIn.page.keyboard.press('Enter')
    await expect(signedIn.page.getByText('transport boundary')).toBeVisible({ timeout: 30_000 })

    // A one-pixel PNG, so the message renders as an image and the client fetches the stored content
    // back — which is what makes "attachment content" a category actually observed rather than
    // merely assumed.
    await signedIn.page.getByTestId('attachment-input').setInputFiles({
      name: 'pixel.png',
      mimeType: 'image/png',
      buffer: ONE_PIXEL_PNG,
    })
    await expect(signedIn.page.getByTestId('attachment-item')).toBeVisible({ timeout: 30_000 })

    // Identity's refresh endpoint is not reached by simply using the app — the access token is
    // still fresh — so ask for it directly through the same client the app uses. The point of the
    // requirement is the path shape, and this is the app's own call site for it.
    await signedIn.page.evaluate(() => fetch('/refresh', { method: 'POST' }).catch(() => {}))

    // Half one: nothing left this origin. A single absolute URL to the backend host would fail
    // here, which is precisely what the requirement forbids.
    //
    // `blob:` and `data:` URLs are excluded because they are not network addresses at all — the
    // image attachment is rendered from an object URL over bytes the page had already fetched
    // through `/api/attachments/`, so it is in-browser memory rather than a call to a host. Neither
    // scheme is capable of addressing the backend directly, which is the thing being ruled out.
    const addressable = traffic.http.filter((url) => /^(https?|wss?):/.test(url))
    const offOrigin = addressable.filter((url) => new URL(url).host !== origin)
    expect(offOrigin, `requests left the frontend's own origin: ${offOrigin.join(', ')}`).toEqual([])

    // The hub's websocket is compared on host and port only: a ws:// URL against an http:// baseURL
    // differs in scheme by definition, and the scheme is not what the requirement constrains.
    const offOriginSockets = traffic.webSocket.filter((url) => new URL(url).host !== origin)
    expect(
      offOriginSockets,
      `websockets left the frontend's own origin: ${offOriginSockets.join(', ')}`
    ).toEqual([])
    // Deliberately not asserting that a websocket *succeeded*. Through the deployed nginx the
    // upgrade does not complete and SignalR falls back to long polling, so the hub's traffic is
    // HTTP on `/hubs/chat`, asserted below. What matters to this requirement is the origin, and the
    // attempted socket is checked for that above whether or not it connects.

    // Half two: every channel the requirement lists was actually exercised, so the assertion above
    // cannot have passed merely because the page contacted nothing.
    const paths = addressable.map((url) => new URL(url).pathname)
    expect(paths.some((p) => p.startsWith('/api/')), 'no REST call observed').toBe(true)
    expect(
      paths.some((p) => p === '/refresh' || p === '/login' || p.startsWith('/manage')),
      'no identity call observed'
    ).toBe(true)
    expect(paths.some((p) => p.startsWith('/hubs/chat')), 'no hub negotiation observed').toBe(true)
    // Attachment *content* is served from `/api/attachments/{id}` rather than the `/uploads` path
    // nginx also proxies — `FE/src/api/attachments.ts` is the only place the frontend fetches it,
    // and it goes through the API. Either way it is a relative path on this origin, which is what
    // the requirement asks; this asserts the one the application actually uses.
    expect(
      paths.some((p) => p.startsWith('/api/attachments/') || p.startsWith('/uploads/')),
      'no attachment content was fetched'
    ).toBe(true)
  }
)

specTest(
  'platform-constraints/deployment-topology/bringing-the-stack-up',
  'the frontend is the only published service, with the backend and database reachable only internally',
  async ({ page, baseURL }) => {
    const frontend = baseURL ?? 'http://localhost:3000'

    // Clause one: the frontend is reachable in a browser.
    await page.goto(frontend)
    await expect(page.locator('#root')).not.toBeEmpty({ timeout: 30_000 })

    // Clauses two and three, as the compose file declares them. Read from `docker-compose.yml`
    // alone and deliberately *without* `docker-compose.tests.yml`: that overlay publishes Postgres
    // on 55432 so the backend integration layer can reach it, which is a test-time concern and not
    // the deployed topology this requirement constrains. Including it here would make the test
    // assert the opposite of the specification.
    const config = await composeConfig()
    const services = Object.keys(config.services)
    expect(services).toEqual(expect.arrayContaining(['db', 'be', 'fe']))

    expect(config.services.be?.ports ?? [], 'the backend publishes a host port').toEqual([])
    expect(config.services.db?.ports ?? [], 'the database publishes a host port').toEqual([])
    expect((config.services.fe?.ports ?? []).length, 'the frontend publishes no host port, so nothing is reachable in a browser')
      .toBeGreaterThan(0)

    // Clause two again, as the running stack behaves rather than as the file declares: the backend
    // does not answer on its own port, while the same API is reachable through the proxy.
    const direct = await probe('http://localhost:8080/api/Rooms/public')
    expect(direct.reachable, `the backend answered directly on 8080 with ${direct.status}`).toBe(
      false
    )

    const proxied = await probe(`${frontend}/api/Rooms/public`)
    expect(proxied.reachable, 'the API was not reachable through the frontend proxy').toBe(true)
    // Unauthenticated, so 401 is the expected answer — what matters is that something answered.
    expect(proxied.status).toBeGreaterThan(0)
  }
)

interface ComposeConfig {
  services: Record<string, { ports?: unknown[] }>
}

/**
 * The resolved compose topology, from `docker compose config` rather than by parsing the YAML.
 *
 * The resolved form is what actually runs — after profiles, defaults, and variable substitution —
 * whereas parsing the file by hand would only assert what someone wrote in it.
 */
async function composeConfig(): Promise<ComposeConfig> {
  try {
    const { stdout } = await run(
      'docker',
      ['compose', '-f', 'docker-compose.yml', 'config', '--format', 'json'],
      { cwd: '..', maxBuffer: 10 * 1024 * 1024 }
    )
    return JSON.parse(stdout) as ComposeConfig
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error)
    throw new Error(
      [
        'This test reads the deployment topology with the docker CLI, and it is not available.',
        '',
        '  Install Docker, or make sure `docker` is on the PATH of the process running the tests.',
        '',
        `  The command failed with: ${message}`,
      ].join('\n')
    )
  }
}

/** Whether a URL answers at all, and with what — a refused connection is the interesting outcome. */
async function probe(url: string): Promise<{ reachable: boolean; status: number }> {
  try {
    const response = await fetch(url, { signal: AbortSignal.timeout(5_000) })
    return { reachable: true, status: response.status }
  } catch {
    return { reachable: false, status: 0 }
  }
}

/** The smallest valid PNG, so the upload is an image without carrying a fixture file around. */
const ONE_PIXEL_PNG = Buffer.from(
  'iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==',
  'base64'
)
