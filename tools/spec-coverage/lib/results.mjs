import { readFile } from 'node:fs/promises'
import { readdir } from 'node:fs/promises'
import { join } from 'node:path'

/**
 * Readers for the three gated layers' machine-readable results.
 *
 * Every reader normalises to the same shape:
 *
 *   { layer, testName, status: 'passed' | 'failed' | 'skipped', scenarioIds: string[] }
 *
 * The scenario identifiers come out of the test's display name, because that is the one field every
 * runner is guaranteed to carry into its report. See design D5.
 */

const MARKER = /@spec:([A-Za-z0-9][A-Za-z0-9-]*(?:\/[A-Za-z0-9][A-Za-z0-9-]*){2})/g

/** Extracts every `@spec:<id>` claim from a test's display name. */
export function extractClaims(testName) {
  return [...testName.matchAll(MARKER)].map((match) => match[1])
}

/** The display name with its claim markers stripped, for readable report output. */
export function stripClaims(testName) {
  return testName.replace(MARKER, '').replace(/\s+/g, ' ').trim()
}

// --- Backend integration: VSTest .trx ---

/**
 * Reads a .trx. Extracted with a regex rather than an XML parser to keep this tool dependency-free;
 * the two attributes needed sit on a single self-describing element.
 */
export function parseTrx(xml) {
  const results = []
  const pattern = /<UnitTestResult\b[^>]*?\btestName="([^"]*)"[^>]*?\boutcome="([^"]*)"/g

  for (const [, rawName, outcome] of xml.matchAll(pattern)) {
    const testName = decodeXml(rawName)
    results.push({
      layer: 'backend',
      testName,
      status: normaliseStatus(outcome),
      scenarioIds: extractClaims(testName),
    })
  }
  return results
}

/** Picks the newest .trx in a directory — `dotnet test` names them after the run. */
export async function findLatestTrx(directory) {
  let entries
  try {
    entries = await readdir(directory, { withFileTypes: true })
  } catch {
    return null
  }
  const trx = entries.filter((e) => e.isFile() && e.name.endsWith('.trx')).map((e) => e.name).sort()
  return trx.length > 0 ? join(directory, trx[trx.length - 1]) : null
}

// --- Frontend unit: vitest JSON ---

export function parseVitestJson(json) {
  const results = []
  for (const file of json.testResults ?? []) {
    for (const assertion of file.assertionResults ?? []) {
      const testName = assertion.fullName ?? assertion.title ?? ''
      results.push({
        layer: 'frontend',
        testName,
        status: normaliseStatus(assertion.status),
        scenarioIds: extractClaims(testName),
      })
    }
  }
  return results
}

// --- End-to-end: Playwright JSON ---

export function parsePlaywrightJson(json) {
  const results = []

  const walk = (suite) => {
    for (const spec of suite.specs ?? []) {
      for (const test of spec.tests ?? []) {
        const outcomes = (test.results ?? []).map((r) => r.status)
        const testName = spec.title ?? ''
        results.push({
          layer: 'e2e',
          testName,
          // A test that was retried counts as passed if any attempt passed, matching how
          // Playwright itself reports it.
          status: outcomes.includes('passed')
            ? 'passed'
            : outcomes.every((o) => o === 'skipped') && outcomes.length > 0
              ? 'skipped'
              : 'failed',
          scenarioIds: extractClaims(testName),
        })
      }
    }
    for (const child of suite.suites ?? []) walk(child)
  }

  for (const suite of json.suites ?? []) walk(suite)
  return results
}

// --- Loading ---

/**
 * Reads whatever result files are present. A layer whose file is missing is reported as "not run"
 * rather than as zero coverage, so running one layer on its own does not look like a regression in
 * the others.
 */
export async function loadResults({ trxDirectory, vitestJson, playwrightJson }) {
  const layers = []

  const trxPath = trxDirectory ? await findLatestTrx(trxDirectory) : null
  layers.push(await readLayer('backend', trxPath, (text) => parseTrx(text)))
  layers.push(await readLayer('frontend', vitestJson, (text) => parseVitestJson(JSON.parse(text))))
  layers.push(await readLayer('e2e', playwrightJson, (text) => parsePlaywrightJson(JSON.parse(text))))

  return {
    layers,
    tests: layers.flatMap((layer) => layer.tests),
  }
}

async function readLayer(name, path, parse) {
  if (!path) return { layer: name, path: null, ran: false, tests: [], error: null }
  try {
    const text = await readFile(path, 'utf8')
    return { layer: name, path, ran: true, tests: parse(text), error: null }
  } catch (error) {
    if (error.code === 'ENOENT') return { layer: name, path, ran: false, tests: [], error: null }
    return { layer: name, path, ran: false, tests: [], error: error.message }
  }
}

// --- helpers ---

/**
 * Anything that is not a pass counts as not covering its claims, per
 * `automated-testing/scenario-traceability/failing-test-does-not-count`.
 */
function normaliseStatus(raw) {
  const value = String(raw).toLowerCase()
  if (value === 'passed') return 'passed'
  if (['skipped', 'notexecuted', 'pending', 'todo', 'ignored', 'inconclusive'].includes(value)) {
    return 'skipped'
  }
  return 'failed'
}

function decodeXml(text) {
  return text
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&#(\d+);/g, (_, code) => String.fromCharCode(Number(code)))
    .replace(/&amp;/g, '&')
}
