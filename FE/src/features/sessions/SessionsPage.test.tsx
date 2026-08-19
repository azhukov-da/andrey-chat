import { describe, expect, vi } from 'vitest'
import { http, HttpResponse } from 'msw'
import { screen, within } from '@testing-library/react'
import { specTest } from '@/test/specTest'
import { renderWithProviders } from '@/test/renderWithProviders'
import { server } from '@/test/msw/server'
import { session } from '@/test/msw/handlers'
import SessionsPage from './SessionsPage'

vi.mock('@/realtime/hubClient', () => import('@/test/fakeHubClient'))

/**
 * Frontend tests for what the sessions screen renders. Which session is "current" is decided by the
 * server (it compares the `X-Session-Id` header against each row), so what is under test here is
 * that the screen surfaces that flag and the device details next to it.
 */
describe('sessions screen', () => {
  specTest(
    'user-sessions/listing-active-sessions/viewing-sessions',
    'lists every session with its device details and marks the current browser "Current"',
    async () => {
      server.use(
        http.get('/api/Sessions', () =>
          HttpResponse.json([
            session({
              id: 'this-browser',
              deviceInfo: 'Win32',
              userAgent: 'Chrome/120',
              ipAddress: '203.0.113.7',
              isCurrent: true,
            }),
            session({
              id: 'old-phone',
              deviceInfo: 'iPhone',
              userAgent: 'Safari/17',
              ipAddress: '198.51.100.4',
              isCurrent: false,
            }),
          ])
        )
      )

      renderWithProviders(<SessionsPage />, { route: '/sessions' })

      const current = (await screen.findByText('Win32')).closest('.card') as HTMLElement
      const other = (await screen.findByText('iPhone')).closest('.card') as HTMLElement

      expect(within(current).getByText('Current')).toBeInTheDocument()
      expect(within(current).getByText('Chrome/120')).toBeInTheDocument()
      expect(within(current).getByText(/203\.0\.113\.7/)).toBeInTheDocument()

      expect(within(other).queryByText('Current')).not.toBeInTheDocument()
      expect(within(other).getByText('Safari/17')).toBeInTheDocument()

      // The action offered differs, because revoking the current session signs this browser out.
      expect(within(current).getByRole('button', { name: 'Sign out' })).toBeInTheDocument()
      expect(within(other).getByRole('button', { name: 'Revoke' })).toBeInTheDocument()
    }
  )
})
