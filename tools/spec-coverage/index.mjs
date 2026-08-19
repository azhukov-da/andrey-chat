#!/usr/bin/env node
/**
 * Scenario traceability: which specified scenarios have a passing test, and which do not.
 *
 * Reads `openspec/specs/**\/spec.md` for the scenarios and the three gated layers' result files for
 * the `@spec:<identifier>` claims their test names carry, then writes
 * `docs/test-coverage/scenarios.md` and prints a summary.
 *
 * Node rather than .NET so it runs without the .NET SDK, and because two of the three result formats
 * are already JSON produced by Node runners (design D6).
 *
 * Exit codes:
 *   0  report written
 *   1  a test claims an identifier that matches no scenario, or the tool could not run
 *
 * Scenario percentage is reported but never gates — only line coverage gates, and that is
 * tools/coverage-gate's job.
 *
 * Usage: node tools/spec-coverage [--specs <dir>] [--out <file>] [--results <dir>]
 */
import { mkdir, writeFile } from 'node:fs/promises'
import { dirname, join, relative, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'
import { loadScenarios } from './lib/scenarios.mjs'
import { loadResults } from './lib/results.mjs'
import { crossReference } from './lib/crossReference.mjs'
import { renderConsole, renderMarkdown } from './lib/report.mjs'

const repoRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..', '..')

function parseArgs(argv) {
  const options = {
    specs: join(repoRoot, 'openspec', 'specs'),
    out: join(repoRoot, 'docs', 'test-coverage', 'scenarios.md'),
    results: join(repoRoot, 'docs', 'test-coverage', 'raw'),
    trx: join(repoRoot, 'BE', 'Tests.Integration', 'TestResults'),
  }
  for (let i = 0; i < argv.length; i += 2) {
    const key = argv[i].replace(/^--/, '')
    if (!(key in options)) {
      throw new Error(`Unknown option "${argv[i]}". Known: ${Object.keys(options).map((k) => `--${k}`).join(', ')}`)
    }
    if (argv[i + 1] === undefined) throw new Error(`Option "${argv[i]}" needs a value.`)
    options[key] = resolve(argv[i + 1])
  }
  return options
}

async function main() {
  const options = parseArgs(process.argv.slice(2))

  const scenarios = await loadScenarios(options.specs)
  if (scenarios.byId.size === 0) {
    throw new Error(
      `No scenarios found under ${options.specs}. Expected capability directories each holding a ` +
        'spec.md with "### Requirement:" and "#### Scenario:" headings.'
    )
  }

  const { layers, tests } = await loadResults({
    trxDirectory: options.trx,
    vitestJson: join(options.results, 'vitest.json'),
    playwrightJson: join(options.results, 'playwright.json'),
  })

  const analysis = crossReference(scenarios, tests)

  await mkdir(dirname(options.out), { recursive: true })
  const generatedAt = new Date().toISOString()
  await writeFile(options.out, renderMarkdown(analysis, layers, generatedAt), 'utf8')

  const shownPath = relative(repoRoot, options.out).replace(/\\/g, '/')
  console.log(renderConsole(analysis, layers, shownPath))

  // A stale claim means a test asserts something the specs no longer describe. That is a defect in
  // the test suite, so it fails the run — see
  // automated-testing/scenario-traceability/test-claiming-an-unknown-scenario.
  if (analysis.unknownClaims.length > 0) process.exitCode = 1
}

main().catch((error) => {
  console.error(`spec-coverage failed: ${error.message}`)
  process.exitCode = 1
})
