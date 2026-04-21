# SignalR Documentation Index

This directory contains comprehensive documentation for the SignalR real-time communication implementation in the Andrey Chat application.

## 📚 Documentation Files

### 1. **[SIGNALR.md](./SIGNALR.md)** - Complete Documentation
The comprehensive guide covering:
- What SignalR endpoints are and how they work
- Complete API reference for all methods and events
- Backend architecture and implementation details
- TypeScript client setup and configuration
- Groups, presence system, and authentication
- Best practices and troubleshooting

**Start here** if you're new to the project or need detailed information.

### 2. **[SIGNALR_QUICK_REFERENCE.md](./SIGNALR_QUICK_REFERENCE.md)** - Quick Reference
A cheat sheet for developers containing:
- Quick code snippets for common operations
- All available methods and events at a glance
- Common patterns and usage examples
- Debugging tips

**Use this** when you need to quickly look up syntax or available methods.

## 📂 Code Files

### 3. **[../FE/src/realtime/signalr-types.ts](../FE/src/realtime/signalr-types.ts)** - Type Definitions
TypeScript type definitions for:
- All server methods (client-to-server)
- All client events (server-to-client)
- Payload interfaces
- Type-safe hub connection wrapper

**Import these types** to get compile-time type safety in your TypeScript code.

### 4. **[../FE/src/realtime/signalr-examples.ts](../FE/src/realtime/signalr-examples.ts)** - Usage Examples
Practical examples demonstrating:
- Basic connection and event handling
- React hooks integration
- Presence tracking
- Unread count management
- Connection state management
- Error handling with retries
- Bulk event registration

**Reference these examples** when implementing new features.

## 🚀 Quick Start

### For Backend Developers (.NET)

1. **Hub Location:** `Web/Hubs/ChatHub.cs`
2. **Notifier Service:** `Web/Hubs/ChatNotifier.cs`
3. **Endpoint Registration:** `Web/Program.cs` (line: `app.MapHub<ChatHub>("/hubs/chat")`)

To add a new server method:
```csharp
// In ChatHub.cs
public async Task YourNewMethod(string param)
{
    // Your logic here
}
```

To broadcast an event:
```csharp
// In ChatNotifier.cs or any service with IHubContext<ChatHub>
await _hubContext.Clients.Group($"room:{roomId}")
    .SendAsync("YourNewEvent", data, cancellationToken);
```

### For Frontend Developers (TypeScript/React)

1. **Connection Management:** `FE/src/realtime/hubClient.ts`
2. **Event Handlers:** `FE/src/realtime/events.ts`
3. **React Hook:** `FE/src/hooks/useSignalR.ts`
4. **Type Definitions:** `FE/src/realtime/signalr-types.ts`

To invoke a server method:
```typescript
const hub = getHubConnection() as TypedHubConnection
await hub.invoke('SendMessage', roomId, text, null)
```

To handle a server event:
```typescript
hub.on('MessageReceived', (message: Message) => {
  console.log('New message:', message)
})
```

## 📋 Available SignalR Endpoints

### Server Methods (Invoke from Client)
| Method | Parameters | Description |
|--------|------------|-------------|
| `Ping` | `active: boolean` | Update presence status |
| `SendMessage` | `roomId, text, replyToMessageId?` | Send a message |
| `EditMessage` | `messageId, text` | Edit a message |
| `DeleteMessage` | `messageId` | Delete a message |
| `MarkRead` | `roomId, messageId` | Mark messages as read |

### Client Events (Received from Server)
| Event | Payload | Target |
|-------|---------|--------|
| `MessageReceived` | `Message` | Room members |
| `MessageEdited` | `{messageId, newText, editedAt}` | Room members |
| `MessageDeleted` | `{messageId}` | Room members |
| `PresenceChanged` | `{userId, status}` | All clients |
| `RoomMembershipChanged` | `{roomId, userId, action}` | Room members |
| `RoomDeleted` | `{roomId}` | Room members |
| `FriendRequestReceived` | `FriendRequest` | Specific user |
| `UnreadUpdated` | `{roomId, unreadCount}` | Specific user |

## 🔍 Common Use Cases

### Send a Message
```typescript
const hub = getHubConnection() as TypedHubConnection
await hub.invoke('SendMessage', roomId, 'Hello!', null)
```

### Listen for New Messages
```typescript
hub.on('MessageReceived', (message) => {
  // Update UI with new message
})
```

### Update User Presence
```typescript
await hub.invoke('Ping', true) // Active
await hub.invoke('Ping', false) // AFK
```

### Handle Unread Counts
```typescript
hub.on('UnreadUpdated', ({ roomId, unreadCount }) => {
  // Update badge or counter
})
```

## 🏗️ Architecture Overview

```
Frontend (React/TypeScript)
    ↓ (WebSocket)
SignalR Hub (/hubs/chat)
    ↓
ChatHub.cs (handles client methods)
    ↓
Application Services
    ↓
ChatNotifier.cs (broadcasts events)
    ↓ (to specific groups)
Connected Clients
```

## 🔐 Authentication

All SignalR connections require JWT authentication:
- Token is provided via `accessTokenFactory` during connection setup
- Token is automatically sent with each request
- Connection is rejected if token is invalid

## 👥 Groups

### User Groups: `user:{userId}`
- Personal notifications
- Friend requests
- Unread count updates

### Room Groups: `room:{roomId}`
- Room messages
- Member changes
- Room events

Users are automatically subscribed to their groups on connection.

## 🐛 Troubleshooting

**Connection fails:**
- Check authentication token is valid
- Verify CORS configuration
- Check network/firewall settings

**Events not received:**
- Ensure handlers are registered before connection starts
- Check user is member of target room/group
- Verify event names match exactly (case-sensitive)

**Multiple connections from same user:**
- Use BroadcastChannel for tab coordination
- Implement leader election pattern (see `FE/src/lib/broadcastChannel.ts`)

## 📖 Further Reading

- [ASP.NET Core SignalR Documentation](https://docs.microsoft.com/en-us/aspnet/core/signalr/)
- [SignalR TypeScript Client](https://www.npmjs.com/package/@microsoft/signalr)
- [SignalR Hub Protocol](https://docs.microsoft.com/en-us/aspnet/core/signalr/hubs)

## 🤝 Contributing

When adding new SignalR functionality:

1. Add server method to `ChatHub.cs`
2. Add event broadcasting to `ChatNotifier.cs` (if needed)
3. Update TypeScript types in `signalr-types.ts`
4. Update event handlers in `events.ts`
5. Add examples to `signalr-examples.ts`
6. Update this documentation

## 📝 Notes

- Hub connection is a singleton (one per browser tab/window)
- Automatic reconnection is configured with exponential backoff
- All server methods can throw `HubException` on error
- Event handlers must be removed on cleanup to prevent memory leaks

---

**Last Updated:** 2024
**Version:** 1.0
**Maintainer:** Development Team
