import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import { server } from './msw/server'
import { resetFakeHub } from './fakeHub'
import { useAuthStore } from '@/stores/authStore'
import { usePresenceStore } from '@/stores/presenceStore'
import { useUnreadStore } from '@/stores/unreadStore'
import { useUiStore } from '@/stores/uiStore'

/**
 * Per-test reset for the frontend layer. Nothing a test does — a store write, a storage write, a
 * handler override, a hub subscription — survives into the next test, which is what makes results
 * independent of execution order.
 */

const stores = [useAuthStore, usePresenceStore, useUnreadStore, useUiStore]

// Captured before any test runs, so resetting cannot pick up a value a test left behind.
const initialStoreStates = stores.map((store) => store.getInitialState())

beforeAll(() => {
  // An unhandled request means a test is reaching for an endpoint the shared handlers do not
  // know about. Failing names the URL; ignoring it would surface as a confusing timeout.
  server.listen({ onUnhandledRequest: 'error' })
})

afterEach(() => {
  cleanup()
  server.resetHandlers()
  resetFakeHub()

  stores.forEach((store, index) => {
    store.setState(initialStoreStates[index], true)
  })

  localStorage.clear()
  sessionStorage.clear()
})

afterAll(() => {
  server.close()
})
