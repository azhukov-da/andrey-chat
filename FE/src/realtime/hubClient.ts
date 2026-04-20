import * as signalR from '@microsoft/signalr'
import { useAuthStore } from '@/stores/authStore'

let connection: signalR.HubConnection | null = null

export function getHubConnection(): signalR.HubConnection {
  if (!connection) {
    connection = new signalR.HubConnectionBuilder()
      .withUrl('/hubs/chat', {
        accessTokenFactory: () => useAuthStore.getState().accessToken ?? '',
      })
      .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
      .configureLogging(signalR.LogLevel.Warning)
      .build()
  }
  return connection
}

export async function startHub(): Promise<void> {
  const hub = getHubConnection()
  if (hub.state === signalR.HubConnectionState.Disconnected) {
    await hub.start()
  }
}

export async function stopHub(): Promise<void> {
  if (connection && connection.state !== signalR.HubConnectionState.Disconnected) {
    await connection.stop()
  }
}
