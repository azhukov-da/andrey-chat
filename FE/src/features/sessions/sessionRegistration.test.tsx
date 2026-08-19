import { describe, expect, vi } from 'vitest'
import { http, HttpResponse } from 'msw'
import { renderHook, waitFor } from '@testing-library/react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider } from 'react-router'
import type { PropsWithChildren, ReactNode } from 'react'
import { specTest } from '@/test/specTest'
import { server } from '@/test/msw/server'
import { testTokens, testUser } from '@/test/msw/handlers'
import { useAuth } from '@/hooks/useAuth'
import { useAuthStore } from '@/stores/authStore'
import { apiFetch } from '@/api/client'

vi.mock('@/realtime/hubClient', () => import('@/test/fakeHubClient'))

/**
 * Frontend tests for the client-side half of `user-sessions`: registering a session for this
 * browser, persisting its identifier, and sending it on later requests. All of that is client
 * behaviour that no server-side test can observe, so it belongs here.
 */
describe('session registration', () => {
  specTest(
    'user-sessions/session-registration-per-browser/new-sign-in-registers-a-session',
    'signing in with no stored session registers one and persists its identifier',
    async () => {
      const registered: (string | null)[] = []
      server.use(
        http.post('/api/Sessions/register', async ({ request }) => {
          const body = (await request.json()) as { deviceInfo: string | null }
          registered.push(body.deviceInfo)
          return HttpResponse.json({ id: 'session-from-sign-in' })
        })
      )

      const { result } = renderAuth()
      await result.current.login('tester@example.test', 'Passw0rd!', false)

      expect(registered).toHaveLength(1)
      expect(localStorage.getItem('sessionId')).toBe('session-from-sign-in')
      expect(useAuthStore.getState().accessToken).toBe(testTokens.accessToken)
    }
  )

  specTest(
    'user-sessions/session-registration-per-browser/new-sign-in-registers-a-session',
    'the stored identifier is sent as X-Session-Id on later requests',
    async () => {
      let seenHeader: string | null = null
      server.use(
        http.get('/api/Sessions', ({ request }) => {
          seenHeader = request.headers.get('X-Session-Id')
          return HttpResponse.json([])
        })
      )

      const { result } = renderAuth()
      await result.current.login('tester@example.test', 'Passw0rd!', false)

      await apiFetch('/api/Sessions')

      expect(seenHeader).toBe('session-1')
    }
  )

  specTest(
    'user-sessions/session-registration-per-browser/restored-login-without-a-session',
    'a restored login with no stored session registers a new one',
    async () => {
      // A returning browser: tokens survived, the session identifier did not.
      sessionStorage.setItem('accessToken', testTokens.accessToken)
      localStorage.setItem('refreshToken', testTokens.refreshToken)
      expect(localStorage.getItem('sessionId')).toBeNull()

      server.use(
        http.post('/api/Sessions/register', () => HttpResponse.json({ id: 'session-after-restore' }))
      )

      const { result } = renderAuth()
      const restored = await result.current.bootstrapAuth()

      expect(restored).toBe(true)
      expect(localStorage.getItem('sessionId')).toBe('session-after-restore')
    }
  )

  specTest(
    'user-sessions/session-registration-per-browser/restored-login-without-a-session',
    'a restored login that already has a session identifier does not register another',
    async () => {
      sessionStorage.setItem('accessToken', testTokens.accessToken)
      localStorage.setItem('refreshToken', testTokens.refreshToken)
      localStorage.setItem('sessionId', 'session-already-known')

      let registrations = 0
      server.use(
        http.post('/api/Sessions/register', () => {
          registrations += 1
          return HttpResponse.json({ id: 'should-not-happen' })
        })
      )

      const { result } = renderAuth()
      await result.current.bootstrapAuth()

      expect(registrations).toBe(0)
      expect(localStorage.getItem('sessionId')).toBe('session-already-known')
    }
  )

  specTest(
    'user-sessions/session-registration-per-browser/session-registration-failure-is-non-fatal',
    'sign-in still completes when session registration fails',
    async () => {
      server.use(
        http.post('/api/Sessions/register', () => new HttpResponse(null, { status: 500 }))
      )

      const { result } = renderAuth()

      // The point of the scenario: this must not throw.
      await expect(
        result.current.login('tester@example.test', 'Passw0rd!', false)
      ).resolves.toBeUndefined()

      expect(localStorage.getItem('sessionId')).toBeNull()
      expect(useAuthStore.getState().accessToken).toBe(testTokens.accessToken)
      expect(useAuthStore.getState().me).toEqual(testUser)

      // And the app stays usable: a request still goes out, just without the session header.
      let seenHeader: string | null = 'not-called'
      server.use(
        http.get('/api/Sessions', ({ request }) => {
          seenHeader = request.headers.get('X-Session-Id')
          return HttpResponse.json([])
        })
      )
      const response = await apiFetch('/api/Sessions')

      expect(response.ok).toBe(true)
      expect(seenHeader).toBeNull()
    }
  )
})

/**
 * `useAuth` needs a router (it navigates on sign-out) and a query client. Rendered as a hook rather
 * than through the sign-in screen because these scenarios are about the auth flow itself, not about
 * the form.
 */
function renderAuth() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  })

  const wrapper = ({ children }: PropsWithChildren) => {
    const router = createMemoryRouter(
      [
        { path: '/', element: children as ReactNode },
        { path: '/login', element: <div data-testid="sign-in-screen" /> },
      ],
      { initialEntries: ['/'] }
    )
    return (
      <QueryClientProvider client={queryClient}>
        <RouterProvider router={router} />
      </QueryClientProvider>
    )
  }

  return { ...renderHook(() => useAuth(), { wrapper }), queryClient, waitFor }
}
