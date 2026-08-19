import type { ReactElement, ReactNode } from 'react'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { createMemoryRouter, RouterProvider } from 'react-router'
import { render, type RenderResult } from '@testing-library/react'
import { useAuthStore } from '@/stores/authStore'
import type { UserProfile } from '@/types'
import { testUser } from './msw/handlers'

export interface RenderOptions {
  /** Initial URL. Route params in components come from here, so `/rooms/room-1` etc. */
  route?: string
  /**
   * Extra routes to mount alongside the component under test. Use when a test asserts that the
   * component navigated somewhere — give the destination a recognisable element.
   */
  extraRoutes?: { path: string; element: ReactNode }[]
  /**
   * Seeds `useAuthStore` so the component renders as a signed-in user. Pass `null` for signed out.
   * Defaults to a signed-in `testUser`, because almost every screen is behind auth.
   */
  auth?: { accessToken?: string; refreshToken?: string; me?: UserProfile } | null
}

export interface RenderWithProvidersResult extends RenderResult {
  queryClient: QueryClient
  /** The current URL, for asserting that a component navigated. */
  currentPath: () => string
}

/**
 * Renders a component with the providers the real app gives it: a React Query client and a
 * router. The query client is fresh per render with retries off and caching disabled, so one
 * test's fetches can never satisfy another's.
 */
export function renderWithProviders(
  ui: ReactElement,
  { route = '/', extraRoutes = [], auth = {} }: RenderOptions = {}
): RenderWithProvidersResult {
  if (auth !== null) seedAuth(auth)

  const queryClient = new QueryClient({
    defaultOptions: {
      queries: { retry: false, staleTime: 0, gcTime: 0 },
      mutations: { retry: false },
    },
  })

  const router = createMemoryRouter(
    [
      { path: route === '/' ? '/' : route, element: ui },
      // Also mount at a wildcard so a component that navigates does not blow up on a missing route.
      ...extraRoutes.map((r) => ({ path: r.path, element: <>{r.element}</> })),
      { path: '*', element: <div data-testid="unmatched-route" /> },
    ],
    { initialEntries: [route] }
  )

  const result = render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  )

  return {
    ...result,
    queryClient,
    currentPath: () => router.state.location.pathname,
  }
}

/**
 * Puts the auth store into a signed-in state without going through the sign-in flow, mirroring
 * what `setTokens`/`setMe` would have left behind — including the storage writes, since
 * `api/client.ts` reads the session id straight out of `localStorage`.
 */
export function seedAuth({
  accessToken = 'test-access-token',
  refreshToken = 'test-refresh-token',
  me = testUser,
}: { accessToken?: string; refreshToken?: string; me?: UserProfile } = {}): void {
  sessionStorage.setItem('accessToken', accessToken)
  sessionStorage.setItem('refreshToken', refreshToken)
  useAuthStore.setState({ accessToken, refreshToken, me, keepSignedIn: false })
}
