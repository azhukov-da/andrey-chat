import { http, HttpResponse } from 'msw'
import type { UserProfile } from '@/types'

/**
 * Default handlers for the endpoints `FE/src/api/*` calls, so a component test only has to
 * declare the responses it actually cares about.
 *
 * Each default is deliberately boring — an empty collection or a minimal object. A test that
 * depends on particular data overrides the handler for that one endpoint:
 *
 * ```ts
 * server.use(
 *   http.get('/api/Sessions', () => HttpResponse.json([session({ isCurrent: true })])),
 * )
 * ```
 *
 * Anything not listed here is an unhandled request, which `onUnhandledRequest: 'error'` turns
 * into a test failure rather than a silent network attempt. That is the point: a new endpoint
 * shows up as a loud failure naming the URL.
 */

// Typed as UserProfile so the fixture cannot drift from the schema `apiJson` parses responses
// with — an extra or missing field here would otherwise only surface as a parse failure at runtime.
export const testUser: UserProfile = {
  id: 'user-1',
  userName: 'tester',
  email: 'tester@example.test',
  displayName: 'Tester',
  createdAt: '2026-01-01T00:00:00.000Z',
}

export const testTokens = {
  tokenType: 'Bearer',
  accessToken: 'test-access-token',
  refreshToken: 'test-refresh-token',
  expiresIn: 3600,
}

/** A session row shaped like `UserSessionDto`, with overridable fields. */
export function session(overrides: Partial<{
  id: string
  deviceInfo: string | null
  userAgent: string | null
  ipAddress: string | null
  createdAt: string
  lastSeenAt: string
  isCurrent: boolean
}> = {}) {
  return {
    id: 'session-1',
    deviceInfo: 'Test device',
    userAgent: 'vitest',
    ipAddress: '127.0.0.1',
    createdAt: '2026-01-01T10:00:00Z',
    lastSeenAt: '2026-01-01T12:00:00Z',
    isCurrent: false,
    ...overrides,
  }
}

const emptyPage = { items: [], nextCursor: null }

export const handlers = [
  // Auth and identity
  http.post('/api/auth/register', () => new HttpResponse(null, { status: 200 })),
  http.post('/api/auth/login', () => HttpResponse.json(testTokens)),
  http.post('/refresh', () => HttpResponse.json(testTokens)),
  http.post('/forgotPassword', () => new HttpResponse(null, { status: 200 })),
  http.post('/resetPassword', () => new HttpResponse(null, { status: 200 })),

  // Current user
  http.get('/api/Me', () => HttpResponse.json(testUser)),
  http.put('/api/Me/display-name', () => new HttpResponse(null, { status: 204 })),
  http.post('/api/Me/password', () => new HttpResponse(null, { status: 204 })),
  http.get('/api/me/invitations', () => HttpResponse.json([])),

  // Sessions
  http.post('/api/Sessions/register', () => HttpResponse.json({ id: 'session-1' })),
  http.get('/api/Sessions', () => HttpResponse.json([])),
  http.delete('/api/Sessions/current', () => new HttpResponse(null, { status: 204 })),
  http.delete('/api/Sessions/:id', () => new HttpResponse(null, { status: 204 })),

  // Rooms
  http.get('/api/Rooms/public', () => HttpResponse.json(emptyPage)),
  http.get('/api/Rooms/mine', () => HttpResponse.json([])),
  http.post('/api/Rooms', () => HttpResponse.json({ id: 'room-1', name: 'Room' }, { status: 201 })),
  http.get('/api/Rooms/:id', () => HttpResponse.json({ id: 'room-1', name: 'Room' })),
  http.put('/api/Rooms/:id', () => new HttpResponse(null, { status: 204 })),
  http.delete('/api/Rooms/:id', () => new HttpResponse(null, { status: 204 })),
  http.post('/api/Rooms/:id/join', () => new HttpResponse(null, { status: 204 })),
  http.post('/api/Rooms/:id/leave', () => new HttpResponse(null, { status: 204 })),
  http.get('/api/Rooms/:id/members', () => HttpResponse.json([])),
  http.delete('/api/Rooms/:id/members/:userId', () => new HttpResponse(null, { status: 204 })),
  http.post('/api/Rooms/:id/members/:userId/make-admin', () => new HttpResponse(null, { status: 204 })),
  http.post('/api/Rooms/:id/members/:userId/remove-admin', () => new HttpResponse(null, { status: 204 })),
  http.get('/api/Rooms/:id/bans', () => HttpResponse.json([])),
  http.post('/api/Rooms/:id/bans/:userId', () => new HttpResponse(null, { status: 204 })),
  http.delete('/api/Rooms/:id/bans/:userId', () => new HttpResponse(null, { status: 204 })),
  http.post('/api/Rooms/:id/invitations', () => new HttpResponse(null, { status: 204 })),

  // Invitations
  http.post('/api/invitations/:id/accept', () => new HttpResponse(null, { status: 204 })),
  http.post('/api/invitations/:id/reject', () => new HttpResponse(null, { status: 204 })),

  // Messages
  http.get('/api/rooms/:roomId/Messages', () => HttpResponse.json(emptyPage)),
  http.post('/api/rooms/:roomId/Messages', () => HttpResponse.json({ id: 'message-1' }, { status: 201 })),
  http.post('/api/rooms/:roomId/Messages/read', () => new HttpResponse(null, { status: 204 })),
  http.delete('/api/messages/:id', () => new HttpResponse(null, { status: 204 })),
  http.put('/api/messages/:id', () => new HttpResponse(null, { status: 204 })),

  // Direct chats, friends, attachments, transcription
  http.get('/api/DirectChats', () => HttpResponse.json([])),
  http.post('/api/DirectChats', () => HttpResponse.json({ id: 'room-dm-1' }, { status: 201 })),
  http.get('/api/Friends', () => HttpResponse.json([])),
  http.get('/api/Friends/requests', () => HttpResponse.json([])),
  http.post('/api/Friends/requests', () => new HttpResponse(null, { status: 204 })),
  http.post('/api/Friends/requests/:userId/accept', () => new HttpResponse(null, { status: 204 })),
  http.post('/api/Friends/requests/:userId/reject', () => new HttpResponse(null, { status: 204 })),
  http.delete('/api/Friends/:userId', () => new HttpResponse(null, { status: 204 })),
  http.post('/api/Friends/blocks/:userId', () => new HttpResponse(null, { status: 204 })),
  http.delete('/api/Friends/blocks/:userId', () => new HttpResponse(null, { status: 204 })),
  http.post('/api/attachments/upload', () => HttpResponse.json({ id: 'attachment-1' }, { status: 201 })),
  http.get('/api/attachments/:id', () => HttpResponse.json({ id: 'attachment-1' })),
  http.post('/api/transcribe', () => HttpResponse.json({ text: '' })),
]
