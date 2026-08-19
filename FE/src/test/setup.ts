import '@testing-library/jest-dom/vitest'
import { cleanup } from '@testing-library/react'
import { afterAll, afterEach, beforeAll } from 'vitest'
import type { StoreApi, UseBoundStore } from 'zustand'
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

/**
 * Captures one store's initial state and returns its reset. Generic per store: collecting the
 * stores into an array first would union four unrelated `setState` signatures into something
 * TypeScript cannot call.
 */
function captureReset<T>(store: UseBoundStore<StoreApi<T>>): () => void {
  // Captured before any test runs, so resetting cannot pick up a value a test left behind.
  const initialState = store.getInitialState()
  return () => store.setState(initialState, true)
}

const resetStores = [
  captureReset(useAuthStore),
  captureReset(usePresenceStore),
  captureReset(useUnreadStore),
  captureReset(useUiStore),
]

beforeAll(() => {
  // An unhandled request means a test is reaching for an endpoint the shared handlers do not
  // know about. Failing names the URL; ignoring it would surface as a confusing timeout.
  server.listen({ onUnhandledRequest: 'error' })
})

afterEach(() => {
  cleanup()
  server.resetHandlers()
  resetFakeHub()

  resetStores.forEach((reset) => reset())

  localStorage.clear()
  sessionStorage.clear()
})

afterAll(() => {
  server.close()
})
