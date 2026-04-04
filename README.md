# BackEndGame

## Realtime presence

- SignalR hub: `/hubs/presence?deviceId={deviceId}`
- When the connection succeeds, the server emits `connection:ready`.
- When the client disconnects, the server removes the connection from the in-memory tracker.
- Presence check API: `GET /api/realtime/presence?deviceId={deviceId}`
