import { test, type TestFunction } from 'vitest'

/**
 * Declares which specified scenarios a test verifies.
 *
 * The claim is written once, here, and the helper prefixes an `@spec:<id>` marker onto the test
 * title. `tools/spec-coverage` reads those markers out of the vitest JSON report, so a claim never
 * has to be repeated in a second place where it could drift.
 *
 * ```ts
 * specTest('user-sessions/session-registration-per-browser/new-sign-in-registers-a-session',
 *   'stores the session id and sends it on later requests', async () => { ... })
 * ```
 *
 * Identifiers come from `openspec/specs/**\/spec.md` as
 * `<capability>/<requirement-slug>/<scenario-slug>`. A claim matching no scenario fails
 * `spec-coverage` by name, so a typo or a renamed scenario surfaces on the next run rather than
 * quietly under-reporting coverage.
 *
 * For a test covering several scenarios, pass an array.
 */
export function specTest(
  scenarioIds: string | string[],
  title: string,
  fn: TestFunction
): void {
  test(specTitle(scenarioIds, title), fn)
}

/** `specTest` variants matching vitest's own modifiers. */
specTest.only = (
  scenarioIds: string | string[],
  title: string,
  fn: TestFunction
): void => {
  test.only(specTitle(scenarioIds, title), fn)
}

specTest.skip = (
  scenarioIds: string | string[],
  title: string,
  fn: TestFunction
): void => {
  test.skip(specTitle(scenarioIds, title), fn)
}

/** The marker prefix plus the human-readable title. Exported for the Playwright helper to reuse. */
export function specTitle(scenarioIds: string | string[], title: string): string {
  const ids = Array.isArray(scenarioIds) ? scenarioIds : [scenarioIds]
  return `${ids.map((id) => `@spec:${id}`).join(' ')} ${title}`
}
