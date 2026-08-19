import { readdir, readFile } from 'node:fs/promises'
import { join } from 'node:path'

/**
 * Turns `openspec/specs/**\/spec.md` into scenario identifiers.
 *
 * The identifiers are derived from the specification files on every run rather than maintained
 * anywhere, which is what makes
 * `automated-testing/scenario-traceability/specification-changes` hold: adding, renaming, or
 * removing a scenario changes the next report with no edit to this tool. The flip side is that a
 * rename invalidates the claims pointing at the old name — deliberately, and loudly, as an unknown
 * claim rather than as silently missing coverage.
 *
 * Format: `<capability>/<requirement-slug>/<scenario-slug>`, e.g.
 * `user-sessions/revoking-sessions/revoking-a-foreign-session`.
 */

/** Lowercase, non-alphanumerics collapsed to a single dash, dashes trimmed off both ends. */
export function slugify(text) {
  return text
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

const REQUIREMENT_HEADING = /^###\s+Requirement:\s*(.+?)\s*$/
const SCENARIO_HEADING = /^####\s+Scenario:\s*(.+?)\s*$/

/**
 * Parses one capability's spec file.
 *
 * @returns {{ capability: string, requirements: Array<{ name: string, slug: string, scenarios: Array<{ name: string, slug: string, id: string, line: number }> }> }}
 */
export function parseSpec(capability, markdown) {
  const requirements = []
  let current = null

  markdown.split(/\r?\n/).forEach((line, index) => {
    const requirement = REQUIREMENT_HEADING.exec(line)
    if (requirement) {
      current = { name: requirement[1], slug: slugify(requirement[1]), scenarios: [] }
      requirements.push(current)
      return
    }

    const scenario = SCENARIO_HEADING.exec(line)
    if (!scenario) return

    if (!current) {
      throw new Error(
        `${capability}/spec.md line ${index + 1}: "#### Scenario: ${scenario[1]}" appears before any ` +
          '"### Requirement:" heading, so it has no identifier. Move it under a requirement.'
      )
    }

    const slug = slugify(scenario[1])
    current.scenarios.push({
      name: scenario[1],
      slug,
      id: `${capability}/${current.slug}/${slug}`,
      line: index + 1,
    })
  })

  return { capability, requirements }
}

/**
 * Reads every capability spec under `specsRoot`.
 *
 * @returns {Promise<{ capabilities: Array<ReturnType<typeof parseSpec>>, byId: Map<string, object> }>}
 */
export async function loadScenarios(specsRoot) {
  const entries = await readdir(specsRoot, { withFileTypes: true })
  const capabilities = []

  for (const entry of entries.filter((e) => e.isDirectory()).sort((a, b) => a.name.localeCompare(b.name))) {
    const path = join(specsRoot, entry.name, 'spec.md')
    let markdown
    try {
      markdown = await readFile(path, 'utf8')
    } catch {
      // A capability directory with no spec.md contributes no scenarios.
      continue
    }
    capabilities.push(parseSpec(entry.name, markdown))
  }

  const byId = new Map()
  for (const capability of capabilities) {
    for (const requirement of capability.requirements) {
      for (const scenario of requirement.scenarios) {
        if (byId.has(scenario.id)) {
          throw new Error(
            `Duplicate scenario identifier "${scenario.id}". Two scenarios under the same ` +
              'requirement slugify to the same name; rename one of them in the spec.'
          )
        }
        byId.set(scenario.id, {
          ...scenario,
          capability: capability.capability,
          requirement: requirement.name,
          requirementSlug: requirement.slug,
        })
      }
    }
  }

  return { capabilities, byId }
}
