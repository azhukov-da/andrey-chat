import type { FullConfig } from '@playwright/test'

/**
 * Asserts the stack is up before a single browser is launched, so a stopped stack produces one
 * clear instruction instead of a screen of navigation timeouts
 * (automated-testing/running-the-suites/missing-prerequisite).
 */
export default async function globalSetup(config: FullConfig): Promise<void> {
  const baseURL = config.projects[0]?.use?.baseURL ?? 'http://localhost:3000'

  let status: number | undefined
  let failure: unknown

  try {
    const response = await fetch(baseURL, { signal: AbortSignal.timeout(10_000) })
    status = response.status
    if (response.ok) return
  } catch (error) {
    failure = error
  }

  throw new Error(
    [
      `End-to-end tests need the full stack serving at ${baseURL}, and it is not responding.`,
      '',
      '  Start it with:',
      '    start.bat',
      '',
      '  or:',
      '    docker compose up -d --build',
      '',
      '  The frontend container must be up, not just the backend — these tests go through its',
      '  nginx proxy, which is itself part of what they verify.',
      '',
      status !== undefined
        ? `  ${baseURL} answered with HTTP ${status}.`
        : `  Could not connect: ${failure instanceof Error ? failure.message : String(failure)}`,
    ].join('\n')
  )
}
