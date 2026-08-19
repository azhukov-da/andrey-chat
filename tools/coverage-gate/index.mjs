#!/usr/bin/env node
/**
 * Evaluates the line-coverage gate for both gated layers.
 *
 * Reads the backend summary ReportGenerator produced from the coverlet output and the frontend
 * `coverage-summary.json` vitest produced, compares each against the threshold, and reports the
 * measured percentage and how far short it falls.
 *
 * By default it reports and exits 0. With `--enforce` a shortfall exits 1, which is what
 * `automated-testing/code-coverage-gate/coverage-below-threshold` requires of the gated run. The
 * flag is off in test.bat for now: with only `user-sessions` tested, coverage is far below the
 * threshold, and failing on it would bury real test failures under a coverage failure. Turning the
 * gate on is adding the flag — see design D7 and the change's Non-Goals.
 *
 * Usage: node tools/coverage-gate [--enforce] [--threshold 80]
 *
 * Exit codes:
 *   0  reported (or met, with --enforce)
 *   1  below threshold and --enforce was passed, or a summary could not be read
 */
import { readFile } from 'node:fs/promises'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')

const DEFAULTS = {
  threshold: 80,
  backendSummary: join(repoRoot, 'docs', 'test-coverage', 'backend', 'Summary.txt'),
  frontendSummary: join(repoRoot, 'docs', 'test-coverage', 'frontend', 'coverage-summary.json'),
}

function parseArgs(argv) {
  const options = { ...DEFAULTS, enforce: false }
  for (let i = 0; i < argv.length; i += 1) {
    const arg = argv[i]
    if (arg === '--enforce') {
      options.enforce = true
    } else if (arg === '--threshold') {
      options.threshold = Number(argv[++i])
      if (!Number.isFinite(options.threshold)) throw new Error('--threshold needs a number.')
    } else if (arg === '--backend-summary') {
      options.backendSummary = resolve(argv[++i])
    } else if (arg === '--frontend-summary') {
      options.frontendSummary = resolve(argv[++i])
    } else {
      throw new Error(`Unknown option "${arg}".`)
    }
  }
  return options
}

/**
 * ReportGenerator's TextSummary writes a line like `Line coverage: 42.1% (123 of 292)`. Older
 * versions omit the counts, so both are accepted.
 */
export function parseBackendSummary(text) {
  const match = /Line coverage:\s*([0-9.]+)%(?:\s*\((\d+)\s+of\s+(\d+)\))?/i.exec(text)
  if (!match) return null
  return {
    percentage: Number(match[1]),
    covered: match[2] ? Number(match[2]) : null,
    total: match[3] ? Number(match[3]) : null,
  }
}

export function parseFrontendSummary(json) {
  const lines = json?.total?.lines
  if (!lines || typeof lines.pct !== 'number') return null
  return { percentage: lines.pct, covered: lines.covered ?? null, total: lines.total ?? null }
}

async function readMeasurement(label, path, parse, hint) {
  try {
    const text = await readFile(path, 'utf8')
    const measurement = parse(text)
    if (!measurement) {
      return { label, path, error: `could not find a line-coverage figure in ${shown(path)}` }
    }
    return { label, path, ...measurement }
  } catch (error) {
    return {
      label,
      path,
      error:
        error.code === 'ENOENT'
          ? `no coverage summary at ${shown(path)} — ${hint}`
          : `could not read ${shown(path)}: ${error.message}`,
    }
  }
}

function shown(path) {
  return relative(repoRoot, path).replace(/\\/g, '/')
}

async function main() {
  const options = parseArgs(process.argv.slice(2))

  const measurements = [
    await readMeasurement(
      'backend',
      options.backendSummary,
      (text) => parseBackendSummary(text),
      'run the backend layer with coverage collection, then ReportGenerator (see test.bat)'
    ),
    await readMeasurement(
      'frontend',
      options.frontendSummary,
      (text) => parseFrontendSummary(JSON.parse(text)),
      'run `npm run test:coverage` in FE'
    ),
  ]

  console.log(`Line coverage gate (threshold ${options.threshold}%)`)
  console.log('-'.repeat(60))

  let shortfalls = 0
  let unreadable = 0

  for (const measurement of measurements) {
    if (measurement.error) {
      unreadable += 1
      console.log(`  ${measurement.label.padEnd(9)} unavailable — ${measurement.error}`)
      continue
    }

    const counts =
      measurement.covered !== null && measurement.total !== null
        ? ` (${measurement.covered} of ${measurement.total} lines)`
        : ''
    const met = measurement.percentage >= options.threshold
    if (met) {
      console.log(
        `  ${measurement.label.padEnd(9)} ${measurement.percentage.toFixed(1).padStart(5)}%${counts}  meets the threshold`
      )
    } else {
      shortfalls += 1
      const short = options.threshold - measurement.percentage
      console.log(
        `  ${measurement.label.padEnd(9)} ${measurement.percentage.toFixed(1).padStart(5)}%${counts}  ` +
          `${short.toFixed(1)} points short of ${options.threshold}%`
      )
    }
  }

  console.log('-'.repeat(60))

  if (unreadable > 0) {
    console.log('  A layer reported as unavailable was not measured. Fix that before reading the')
    console.log('  numbers above as a verdict.')
    process.exitCode = 1
    return
  }

  if (shortfalls === 0) {
    console.log('  Both layers meet the threshold.')
    return
  }

  if (options.enforce) {
    console.log(`  ${shortfalls} layer(s) below ${options.threshold}%. Failing the run.`)
    process.exitCode = 1
  } else {
    console.log(`  ${shortfalls} layer(s) below ${options.threshold}%. Reported, not enforced.`)
    console.log('  Pass --enforce to make a shortfall fail the run. Expected to stay off until the')
    console.log('  per-capability test changes land — see the automated-testing spec.')
  }
}

main().catch((error) => {
  console.error(`coverage-gate failed: ${error.message}`)
  process.exitCode = 1
})
