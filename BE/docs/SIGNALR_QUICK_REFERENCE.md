# SignalR Quick Reference

## ?? Connection

**Endpoint:** `/hubs/chat`

**TypeScript:**
```typescript
import { getHubConnection } from '@/realtime/hubClient'

const hub = getHubConnection()
await hub.start()
```

## ?? Authentication

Requires JWT bearer token via `accessTokenFactory`:

```typescript
.withUrl('/hubs/chat', {
  accessTokenFactory: () => yourAccessToken
})
```

## ?? Invoke Server Methods

### Send Message
```typescript
await hub.invoke('SendMessage', roomId, text, replyToMessageId)
```

### Edit Message
```typescript
await hub.invoke('EditMessage', messageId, newText)
```

### Delete Message
```typescript
await hub.invoke('DeleteMessage', messageId)
```

### Mark as Read
```typescript
await hub.invoke('MarkRead', roomId, lastMessageId)
```

### Ping (Presence)
```typescript
await hub.invoke('Ping', isActive)
```

## ?? Receive Server Events

### Message Received
```typescript
hub.on('MessageReceived', (message) => {
  console.log('New message:', message)
})
```

### Message Edited
```typescript
hub.on('MessageEdited', ({ messageId, newText, editedAt }) => {
  console.log('Message edited:', messageId)
})
```

### Message Deleted
```typescript
hub.on('MessageDeleted', ({ messageId }) => {
  console.log('Message deleted:', messageId)
})
```

### Presence Changed
```typescript
hub.on('PresenceChanged', ({ userId, status }) => {
  console.log(`${userId} is now ${status}`)
})
```

### Room Membership Changed
```typescript
hub.on('RoomMembershipChanged', ({ roomId, userId, action }) => {
  console.log(`${userId} ${action} room ${roomId}`)
})
```

### Room Deleted
```typescript
hub.on('RoomDeleted', ({ roomId }) => {
  console.log('Room deleted:', roomId)
})
```

### Friend Request Received
```typescript
hub.on('FriendRequestReceived', (friendRequest) => {
  console.log('New friend request:', friendRequest)
})
```

### Unread Count Updated
```typescript
hub.on('UnreadUpdated', ({ roomId, unreadCount }) => {
  console.log(`Room ${roomId}: ${unreadCount} unread`)
})
```

## ?? Cleanup

Always unregister event handlers:

```typescript
const handler = (message) => { /* ... */ }

hub.on('MessageReceived', handler)

// Later...
hub.off('MessageReceived', handler)

// Stop connection
await hub.stop()
```

## ?? Automatic Reconnection

Configured with delays: 0ms, 1s, 2s, 5s, 10s

```typescript
.withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
```

## ?? Groups

### User Group
**Format:** `user:{userId}`
- Personal notifications
- Unread updates
- Friend requests

### Room Group
**Format:** `room:{roomId}`
- Room messages
- Member changes
- Room deletion

Users are automatically added to their groups on connection.

## ?? Error Handling

```typescript
try {
  await hub.invoke('SendMessage', roomId, text)
} catch (error) {
  console.error('Failed to send:', error.message)
}
```

## ?? Type Safety

Import types for compile-time safety:

```typescript
import type { 
  TypedHubConnection,
  Message,
  MessageEditedPayload 
} from '@/realtime/signalr-types'

const hub = getHubConnection() as TypedHubConnection
```

## ?? Connection States

- `Disconnected` - Not connected
- `Connecting` - Establishing connection
- `Connected` - Active connection
- `Reconnecting` - Attempting to reconnect

Check state:
```typescript
if (hub.state === HubConnectionState.Connected) {
  // Do something
}
```

## ?? React Hook

Use the provided hook for automatic lifecycle management:

```typescript
import { useSignalR } from '@/hooks/useSignalR'

function App() {
  useSignalR()  // Handles connect/disconnect automatically
  
  return <YourApp />
}
```

## ?? Debugging

Enable detailed logging:

```typescript
.configureLogging(signalR.LogLevel.Debug)
```

Levels: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, `None`

## ?? Backend Hub Location

`Web/Hubs/ChatHub.cs` - Main hub implementation
`Web/Hubs/ChatNotifier.cs` - Notification service (uses `IHubContext`)

## ?? Useful Links

- [Full Documentation](./SIGNALR.md)
- [Microsoft SignalR Docs](https://docs.microsoft.com/en-us/aspnet/core/signalr/)
- [SignalR TypeScript Client](https://www.npmjs.com/package/@microsoft/signalr)
