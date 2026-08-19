import { stripClaims } from './results.mjs'

/**
 * Cross-references the specified scenarios against what the test results claim.
 *
 * Only passing tests count. A failed or skipped test's claims are recorded separately so the report
 * can say "claimed but the test did not pass", which is more useful than silently showing the
 * scenario as uncovered.
 */
export function crossReference({ capabilities, byId }, tests) {
  /** @type {Map<string, Array<{layer: string, testName: string, status: string}>>} */
  const claimsById = new Map()
  const unknownClaims = []

  for (const test of tests) {
    for (const scenarioId of test.scenarioIds) {
      if (!byId.has(scenarioId)) {
        unknownClaims.push({ scenarioId, layer: test.layer, testName: test.testName })
        continue
      }
      const claims = claimsById.get(scenarioId) ?? []
      claims.push({ layer: test.layer, testName: stripClaims(test.testName), status: test.status })
      claimsById.set(scenarioId, claims)
    }
  }

  const perCapability = capabilities.map((capability) => {
    const scenarios = capability.requirements.flatMap((requirement) =>
      requirement.scenarios.map((scenario) => {
        const claims = claimsById.get(scenario.id) ?? []
        const passing = claims.filter((c) => c.status === 'passed')
        return {
          ...scenario,
          requirement: requirement.name,
          claims,
          passing,
          covered: passing.length > 0,
          claimedButNotPassing: passing.length === 0 && claims.length > 0,
          layers: [...new Set(passing.map((c) => c.layer))].sort(),
        }
      })
    )

    const covered = scenarios.filter((s) => s.covered).length
    return {
      capability: capability.capability,
      requirements: capability.requirements.map((r) => r.name),
      scenarios,
      total: scenarios.length,
      covered,
      percentage: scenarios.length === 0 ? 0 : (covered / scenarios.length) * 100,
    }
  })

  const total = perCapability.reduce((sum, c) => sum + c.total, 0)
  const covered = perCapability.reduce((sum, c) => sum + c.covered, 0)

  return {
    perCapability,
    unknownClaims,
    total,
    covered,
    percentage: total === 0 ? 0 : (covered / total) * 100,
  }
}
