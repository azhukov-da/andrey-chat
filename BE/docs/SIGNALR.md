# SignalR Endpoints Documentation

## Overview

This application uses **SignalR** for real-time, bidirectional communication between the .NET backend and TypeScript/React frontend. SignalR enables the server to push notifications to connected clients instantly without polling.

## Hub Endpoint

**Endpoint URL:** `/hubs/chat`

The application exposes a single SignalR hub at this endpoint. All real-time communication flows through this hub.

### Backend Implementation

Location: `Web\Hubs\ChatHub.cs`

## Authentication

The SignalR hub requires authentication using the `[Authorize]` attribute. Clients must provide a valid JWT bearer token when connecting.

## Connection Flow

### Backend Connection Lifecycle

When a client connects:
1. User is added to presence tracking
2. User is added to their personal group: `user:{userId}`
3. User is automatically added to all room groups they're a member of: `room:{roomId}`

When a client disconnects:
- User is removed from presence tracking

## Client-to-Server Methods (Invocable)

These are methods that the TypeScript client can **invoke** on the server:

### 1. `Ping`
Updates the user's activity status (active/AFK).

**Parameters:**
- `active` (boolean) - Whether the user is currently active

**TypeScript Usage:**
```typescript
await hub.invoke('Ping', true)
```

**Implementation:** Updates presence tracker with current activity status

---

### 2. `SendMessage`
Sends a new message to a room.

**Parameters:**
- `roomId` (Guid) - The ID of the room
- `text` (string) - The message text
- `replyToMessageId` (Guid?, optional) - ID of message being replied to

**TypeScript Usage:**
```typescript
await hub.invoke('SendMessage', roomId, messageText, replyToMessageId)
```

**Throws:** `HubException` if the operation fails

**Side Effect:** Triggers `MessageReceived` event to all room members

---

### 3. `EditMessage`
Edits an existing message.

**Parameters:**
- `messageId` (Guid) - The ID of the message to edit
- `text` (string) - The new message text

**TypeScript Usage:**
```typescript
await hub.invoke('EditMessage', messageId, newText)
```

**Throws:** `HubException` if the operation fails

**Side Effect:** Triggers `MessageEdited` event to all room members

---

### 4. `DeleteMessage`
Deletes a message.

**Parameters:**
- `messageId` (Guid) - The ID of the message to delete

**TypeScript Usage:**
```typescript
await hub.invoke('DeleteMessage', messageId)
```

**Throws:** `HubException` if the operation fails

**Side Effect:** Triggers `MessageDeleted` event to all room members

---

### 5. `MarkRead`
Marks messages as read up to a specific message in a room.

**Parameters:**
- `roomId` (Guid) - The ID of the room
- `messageId` (Guid) - The ID of the last message read

**TypeScript Usage:**
```typescript
await hub.invoke('MarkRead', roomId, lastMessageId)
```

**Throws:** `HubException` if the operation fails

**Side Effect:** Triggers `UnreadUpdated` event to the user

---

## Server-to-Client Events (Receivable)

These are events that the server **sends** to connected TypeScript clients:

### 1. `MessageReceived`
Broadcasted when a new message is sent to a room.

**Target:** All members of the room group (`room:{roomId}`)

**Payload:**
```typescript
{
  id: string
  roomId: string
  senderId: string
  text: string
  createdAt: string
  replyToMessageId?: string
  // ... full Message object
}
```

**TypeScript Handler:**
```typescript
hub.on('MessageReceived', (message: Message) => {
  // Handle new message
})
```

---

### 2. `MessageEdited`
Broadcasted when a message is edited.

**Target:** All members of the room group (`room:{roomId}`)

**Payload:**
```typescript
{
  messageId: string
  newText: string
  editedAt: string
}
```

**TypeScript Handler:**
```typescript
hub.on('MessageEdited', (payload: MessageEditedPayload) => {
  // Update message in UI
})
```

---

### 3. `MessageDeleted`
Broadcasted when a message is deleted.

**Target:** All members of the room group (`room:{roomId}`)

**Payload:**
```typescript
{
  messageId: string
}
```

**TypeScript Handler:**
```typescript
hub.on('MessageDeleted', (payload: MessageDeletedPayload) => {
  // Mark message as deleted in UI
})
```

---

### 4. `PresenceChanged`
Broadcasted when a user's presence status changes (online/AFK/offline).

**Target:** All connected clients

**Payload:**
```typescript
{
  userId: string
  status: string  // "Online" | "Afk" | "Offline"
}
```

**TypeScript Handler:**
```typescript
hub.on('PresenceChanged', (payload: PresenceChangedPayload) => {
  // Update user's presence indicator
})
```

---

### 5. `RoomMembershipChanged`
Broadcasted when a user joins or leaves a room.

**Target:** All members of the room group (`room:{roomId}`)

**Payload:**
```typescript
{
  roomId: string
  userId: string
  action: string  // e.g., "joined", "left"
}
```

**TypeScript Handler:**
```typescript
hub.on('RoomMembershipChanged', (payload: RoomMembershipChangedPayload) => {
  // Refresh room members list
})
```

---

### 6. `RoomDeleted`
Broadcasted when a room is deleted.

**Target:** All members of the room group (`room:{roomId}`)

**Payload:**
```typescript
{
  roomId: string
}
```

**TypeScript Handler:**
```typescript
hub.on('RoomDeleted', (payload: RoomDeletedPayload) => {
  // Navigate away from deleted room
})
```

---

### 7. `FriendRequestReceived`
Sent when the user receives a new friend request.

**Target:** Specific user group (`user:{userId}`)

**Payload:**
```typescript
{
  // Full friend request DTO
  id: string
  fromUserId: string
  // ... other friend request fields
}
```

**TypeScript Handler:**
```typescript
hub.on('FriendRequestReceived', (friendRequest: FriendRequest) => {
  // Show notification or update friend requests list
})
```

---

### 8. `UnreadUpdated`
Sent when a room's unread count changes for a user.

**Target:** Specific user group (`user:{userId}`)

**Payload:**
```typescript
{
  roomId: string
  unreadCount: number
}
```

**TypeScript Handler:**
```typescript
hub.on('UnreadUpdated', (payload: UnreadUpdatedPayload) => {
  // Update unread badge
})
```

---

## TypeScript Client Setup

### 1. Installation

```bash
npm install @microsoft/signalr
```

### 2. Connection Setup

**File:** `FE/src/realtime/hubClient.ts`

```typescript
import * as signalR from '@microsoft/signalr'

const connection = new signalR.HubConnectionBuilder()
  .withUrl('/hubs/chat', {
    accessTokenFactory: () => authToken,
  })
  .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
  .configureLogging(signalR.LogLevel.Warning)
  .build()

await connection.start()
```

### 3. Event Registration

**File:** `FE/src/realtime/events.ts`

Register all event handlers before starting the connection:

```typescript
hub.on('MessageReceived', onMessageReceived)
hub.on('MessageEdited', onMessageEdited)
// ... register all other events
```

### 4. React Hook

**File:** `FE/src/hooks/useSignalR.ts`

The `useSignalR()` hook manages the SignalR lifecycle:
- Connects when user is authenticated
- Registers event handlers
- Starts presence ping mechanism
- Handles reconnection
- Cleans up on logout/unmount

Usage:
```typescript
function App() {
  useSignalR()  // Initialize SignalR connection
  return <YourApp />
}
```

---

## Groups

SignalR uses groups to efficiently target specific sets of clients:

### User Group
**Format:** `user:{userId}`

**Purpose:** Send notifications to a specific user across all their connections

**Use Cases:**
- Friend request notifications
- Unread count updates
- Personal notifications

### Room Group
**Format:** `room:{roomId}`

**Purpose:** Send messages/events to all members of a room

**Use Cases:**
- New messages in room
- Message edits/deletes
- Member join/leave events
- Room deletion

---

## Presence System

The application includes a sophisticated presence tracking system:

### Client-Side Ping
- Every 30 seconds, clients send a `Ping(active)` to update their status
- AFK detection monitors user activity (mouse/keyboard)
- Status is automatically set to `afk` after inactivity

### Server-Side Tracking
- `IPresenceTracker` maintains connection states
- Broadcasts `PresenceChanged` events when status changes
- Tracks multiple connections per user

### Leader Election
Uses BroadcastChannel API to elect a "leader" tab that sends pings, preventing duplicate pings from multiple tabs.

---

## Error Handling

### HubException
Server methods throw `HubException` for business logic errors. Handle these on the client:

```typescript
try {
  await hub.invoke('SendMessage', roomId, text)
} catch (error) {
  if (error.message) {
    console.error('Failed:', error.message)
  }
}
```

### Automatic Reconnection
The connection is configured with automatic reconnection delays:
- Immediate (0ms)
- 1 second
- 2 seconds
- 5 seconds
- 10 seconds

After exhausting retries, manual reconnection is required.

---

## Best Practices

### 1. Connection Management
- Only maintain one connection per browser window
- Use the singleton pattern for hub connection
- Always clean up event handlers on unmount

### 2. Event Handlers
- Keep handlers lightweight
- Use optimistic updates where possible
- Invalidate/refetch queries after mutations

### 3. Authentication
- Always provide fresh token via `accessTokenFactory`
- Handle 401 errors by refreshing token or re-authenticating
- Disconnect on logout

### 4. TypeScript Types
- Define payload interfaces for type safety
- Use generics for query data types
- Leverage discriminated unions for status enums

---

## Troubleshooting

### Connection Issues
1. Check authentication token is valid
2. Verify CORS configuration allows SignalR
3. Check browser console for connection errors
4. Verify backend hub is registered: `app.MapHub<ChatHub>("/hubs/chat")`

### Events Not Received
1. Verify event handler is registered before connection starts
2. Check user is member of target room/group
3. Confirm server is broadcasting to correct group
4. Check for typos in event names (case-sensitive)

### Multiple Connections
- Use BroadcastChannel or similar for tab coordination
- Implement leader election for resource-intensive operations
- Consider using connection ID for debugging

---

## Architecture Diagram

```
┌─────────────────────────────────────────────────────┐
│                   TypeScript Client                  │
│                                                      │
│  ┌──────────────┐    ┌────────────────────────┐   │
│  │ useSignalR() │───▶│  Hub Connection        │   │
│  │   Hook       │    │  /hubs/chat           │   │
│  └──────────────┘    └────────────────────────┘   │
│                               │                     │
│  ┌───────────────────────────┼──────────────────┐ │
│  │         Event Handlers    │                  │ │
│  │  • MessageReceived        │                  │ │
│  │  • MessageEdited          │                  │ │
│  │  • PresenceChanged        │                  │ │
│  │  • ...                    │                  │ │
│  └───────────────────────────┼──────────────────┘ │
└────────────────────────────────┼───────────────────┘
                                 │
                        SignalR WebSocket
                                 │
┌────────────────────────────────┼───────────────────┐
│                    .NET Backend │                   │
│                                                     │
│  ┌──────────────────────────────────────────────┐ │
│  │            ChatHub                            │ │
│  │  • OnConnectedAsync()                        │ │
│  │  • OnDisconnectedAsync()                     │ │
│  │  • SendMessage(), EditMessage(), etc.        │ │
│  └──────────────────────────────────────────────┘ │
│                         │                          │
│  ┌──────────────────────┼────────────────────────┐│
│  │     ChatNotifier     │                        ││
│  │     (IHubContext)    │                        ││
│  │  • MessageReceived    │                       ││
│  │  • PresenceChanged    │                       ││
│  │  • ...                │                       ││
│  └───────────────────────┼────────────────────────┘│
│                          │                         │
│  ┌───────────────────────▼──────────────────────┐ │
│  │          Application Services                 │ │
│  │  • MessageService                            │ │
│  │  • PresenceTracker                           │ │
│  │  • FriendService                             │ │
│  └──────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────┘
```

---

## Summary

**SignalR Hub URL:** `/hubs/chat`

**Client Methods (Invokable):**
- `Ping(bool active)`
- `SendMessage(Guid roomId, string text, Guid? replyTo)`
- `EditMessage(Guid messageId, string text)`
- `DeleteMessage(Guid messageId)`
- `MarkRead(Guid roomId, Guid messageId)`

**Server Events (Receivable):**
- `MessageReceived` - New message in room
- `MessageEdited` - Message edited
- `MessageDeleted` - Message deleted
- `PresenceChanged` - User status changed
- `RoomMembershipChanged` - User joined/left room
- `RoomDeleted` - Room deleted
- `FriendRequestReceived` - New friend request
- `UnreadUpdated` - Unread count changed

**Groups:**
- `user:{userId}` - Personal notifications
- `room:{roomId}` - Room events

**Authentication:** JWT Bearer token required via `accessTokenFactory`
