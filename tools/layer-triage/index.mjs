#!/usr/bin/env node
/**
 * Renders `docs/test-layer-triage.md` from `openspec/specs/**\/spec.md` and `assignments.json`.
 *
 * The `automated-testing` capability requires every scenario to sit at the layer that observes it
 * most directly. That decision is a judgement call, and a judgement call made silently while
 * writing a test is a judgement call nobody can review. So it is made here instead: one entry per
 * requirement (or per scenario, where a requirement splits across layers), each carrying a reason.
 *
 * The tool exists for the cross-check, not for the prose. It fails when
 *
 *   - a specified scenario resolves to no assignment — the scenario would otherwise vanish from the
 *     work-list without anyone noticing it had;
 *   - an assignment matches no specified scenario — a stale key left behind by a spec rename;
 *   - an assignment names a layer outside the four the spec defines.
 *
 * Same contract as `tools/spec-coverage`: identifiers are re-derived from the spec files on every
 * run, so a rename surfaces as a named failure rather than as quiet drift.
 *
 * Usage: node tools/layer-triage [--check]
 *   --check  verify the committed document is up to date without rewriting it
 */

import { readFile, writeFile } from 'node:fs/promises'
import { dirname, join, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

import { loadScenarios } from '../spec-coverage/lib/scenarios.mjs'

const here = dirname(fileURLToPath(import.meta.url))
const repoRoot = resolve(here, '..', '..')
const specsRoot = join(repoRoot, 'openspec', 'specs')
const assignmentsPath = join(here, 'assignments.json')
const outputPath = join(repoRoot, 'docs', 'test-layer-triage.md')

/** The layers the `automated-testing` capability defines, plus the two non-test dispositions. */
const LAYERS = {
  backend: 'Backend integration',
  frontend: 'Frontend unit / component',
  e2e: 'End-to-end',
  load: 'Load and capacity (not built)',
  tooling: 'Verified by the tooling itself',
}

async function main() {
  const check = process.argv.includes('--check')
  const { capabilities, byId } = await loadScenarios(specsRoot)
  const assignments = JSON.parse(await readFile(assignmentsPath, 'utf8'))

  const problems = []
  const used = new Set()

  /** Scenario key wins over requirement key, so a split requirement needs only its exceptions listed. */
  const resolve_ = (scenario) => {
    const requirementKey = `${scenario.capability}/${scenario.requirementSlug}`
    if (Object.hasOwn(assignments, scenario.id)) {
      used.add(scenario.id)
      return assignments[scenario.id]
    }
    if (Object.hasOwn(assignments, requirementKey)) {
      used.add(requirementKey)
      return assignments[requirementKey]
    }
    return null
  }

  const rows = []
  for (const capability of capabilities) {
    for (const requirement of capability.requirements) {
      for (const scenario of requirement.scenarios) {
        const full = byId.get(scenario.id)
        const assignment = resolve_(full)
        if (!assignment) {
          problems.push(`No assignment for "${scenario.id}". Add it to tools/layer-triage/assignments.json.`)
          continue
        }
        if (!Object.hasOwn(LAYERS, assignment.layer)) {
          problems.push(
            `"${scenario.id}" is assigned to unknown layer "${assignment.layer}". ` +
              `Expected one of: ${Object.keys(LAYERS).join(', ')}.`
          )
          continue
        }
        rows.push({ ...full, ...assignment })
      }
    }
  }

  for (const key of Object.keys(assignments)) {
    if (key.startsWith('_')) continue // `_comment` and friends carry the file's own documentation.
    if (!used.has(key)) {
      problems.push(
        `Assignment "${key}" matches no specified scenario or requirement. ` +
          'It is stale — a spec heading was probably renamed.'
      )
    }
  }

  if (problems.length > 0) {
    console.error('Layer triage is out of step with the specifications:\n')
    for (const problem of problems) console.error(`  - ${problem}`)
    console.error(`\n${problems.length} problem(s).`)
    process.exit(1)
  }

  const markdown = render(rows, assignments._capabilityNotes ?? {})

  if (check) {
    let existing = ''
    try {
      existing = await readFile(outputPath, 'utf8')
    } catch {
      /* falls through to the mismatch report */
    }
    if (existing !== markdown) {
      console.error(
        `${outputPath} is out of date. Run \`node tools/layer-triage\` and commit the result.`
      )
      process.exit(1)
    }
    console.log(`${rows.length} scenarios triaged; document is up to date.`)
    return
  }

  await writeFile(outputPath, markdown, 'utf8')
  console.log(`Wrote ${outputPath} — ${rows.length} scenarios triaged.`)
  for (const [layer, label] of Object.entries(LAYERS)) {
    const count = rows.filter((r) => r.layer === layer).length
    console.log(`  ${label}: ${count}`)
  }
}

function render(rows, capabilityNotes) {
  const lines = []
  lines.push('# Test layer triage')
  lines.push('')
  lines.push(
    'Which layer owns which specified scenario. Generated by `tools/layer-triage` from',
    '`openspec/specs/**/spec.md` and `tools/layer-triage/assignments.json` — do not edit by hand;',
    'edit the assignments and re-run.'
  )
  lines.push('')
  lines.push(
    'The `automated-testing` capability requires each scenario to be verified at the layer that can',
    'observe its outcome most directly, and does not require a scenario observable at several layers',
    'to appear at all of them. This table is where that call is recorded, with a reason, so an',
    'omission is reviewable rather than invisible.'
  )
  lines.push('')
  lines.push('| Layer | Meaning |')
  lines.push('|---|---|')
  lines.push('| Backend integration | A rule the server enforces regardless of which client calls it |')
  lines.push('| Frontend unit / component | What the interface shows, enables, or blocks before contacting the server |')
  lines.push('| End-to-end | Spans browser, proxy, server, and database together |')
  lines.push('| Load and capacity (not built) | Specified, deliberately outside the gated run |')
  lines.push('| Verified by the tooling itself | The test machinery is its own subject |')
  lines.push('')

  lines.push('## Totals')
  lines.push('')
  lines.push('| Layer | Scenarios |')
  lines.push('|---|---:|')
  for (const [layer, label] of Object.entries(LAYERS)) {
    lines.push(`| ${label} | ${rows.filter((r) => r.layer === layer).length} |`)
  }
  lines.push(`| **total** | **${rows.length}** |`)
  lines.push('')

  const capabilities = [...new Set(rows.map((r) => r.capability))]
  for (const capability of capabilities) {
    const own = rows.filter((r) => r.capability === capability)
    const backend = own.filter((r) => r.layer === 'backend').length
    lines.push(`## ${capability}`)
    lines.push('')
    lines.push(`${backend} of ${own.length} scenarios sit at the backend integration layer.`)
    lines.push('')
    // A capability whose split needs explaining beyond the per-row reasons carries a note in
    // `_capabilityNotes`. Keeping it in the assignments file keeps this document generated.
    const note = capabilityNotes[capability]
    if (note) {
      lines.push(Array.isArray(note) ? note.join('\n') : note)
      lines.push('')
    }
    lines.push('| Scenario | Layer | Why |')
    lines.push('|---|---|---|')
    for (const row of own) {
      lines.push(`| \`${row.id}\` | ${LAYERS[row.layer]} | ${row.why} |`)
    }
    lines.push('')
  }

  return lines.join('\n')
}

main().catch((error) => {
  console.error(error)
  process.exit(1)
})
