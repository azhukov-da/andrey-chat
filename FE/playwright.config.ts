import { defineConfig, devices } from '@playwright/test'

/**
 * End-to-end suite. Drives the assembled Docker Compose stack through the frontend's own nginx at
 * http://localhost:3000 — the only address where the real proxy configuration in `nginx.conf` is
 * part of what is under test.
 *
 * There is deliberately no `webServer` block: this suite does not launch anything. `start.bat`
 * brings the stack up, and `e2e/global-setup.ts` fails with an actionable message when it is not
 * running (automated-testing/running-the-suites/missing-prerequisite).
 */
export default defineConfig({
  testDir: './e2e',
  testMatch: '**/*.spec.ts',
  globalSetup: './e2e/global-setup.ts',

  // The stack is shared, so a run that hangs should not hang forever.
  timeout: 60_000,
  expect: { timeout: 10_000 },

  // Every test registers its own users, so parallelism is safe. Kept low deliberately: these tests
  // share one Docker stack with the rest of the run, and at four workers the same tests went from
  // ~5s to ~25s and started brushing against their timeouts. Two workers is the honest setting —
  // retries would hide that rather than fix it.
  fullyParallel: true,
  workers: 2,
  retries: 0,

  reporter: [
    ['list'],
    // Machine-readable results for tools/spec-coverage, which reads the @spec: markers out of the
    // test titles in this file.
    ['json', { outputFile: '../docs/test-coverage/raw/playwright.json' }],
    ['html', { outputFolder: '../docs/test-coverage/e2e-report', open: 'never' }],
  ],

  outputDir: '../docs/test-coverage/raw/playwright-artifacts',

  use: {
    baseURL: process.env.E2E_BASE_URL ?? 'http://localhost:3000',
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'off',
  },

  projects: [
    { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  ],
})
