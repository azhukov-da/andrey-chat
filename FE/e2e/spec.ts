import { test } from './fixtures'

/**
 * Declares which specified scenarios an end-to-end test verifies.
 *
 * Same shape and same mechanism as the vitest helper in `src/test/specTest.ts`: the claim is
 * written once and the helper prefixes an `@spec:<id>` marker onto the test title, where
 * `tools/spec-coverage` finds it in Playwright's JSON report.
 *
 * ```ts
 * specTest('user-sessions/revoking-sessions/revoking-another-session',
 *   'the revoked row disappears and this browser stays signed in',
 *   async ({ signedIn }) => { ... })
 * ```
 */
export function specTest(
  scenarioIds: string | string[],
  title: string,
  fn: Parameters<typeof test>[1]
): void {
  test(specTitle(scenarioIds, title), fn)
}

specTest.only = (
  scenarioIds: string | string[],
  title: string,
  fn: Parameters<typeof test>[1]
): void => {
  test.only(specTitle(scenarioIds, title), fn)
}

specTest.skip = (
  scenarioIds: string | string[],
  title: string,
  fn: Parameters<typeof test>[1]
): void => {
  test.skip(specTitle(scenarioIds, title), fn)
}

export function specTitle(scenarioIds: string | string[], title: string): string {
  const ids = Array.isArray(scenarioIds) ? scenarioIds : [scenarioIds]
  return `${ids.map((id) => `@spec:${id}`).join(' ')} ${title}`
}
