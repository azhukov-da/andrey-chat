import { setupServer } from 'msw/node'
import { handlers } from './handlers'

/**
 * The request interceptor shared by every frontend test. Started once in
 * `src/test/setup.ts`; per-test overrides go through `server.use(...)` and are undone after each
 * test, so no test can leak a handler into the next one.
 */
export const server = setupServer(...handlers)
