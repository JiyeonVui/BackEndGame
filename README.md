# BackEndGame

## Database

- The API now uses PostgreSQL through Entity Framework Core.
- Configure the connection string in `appsettings.json` or `appsettings.Development.json` under `ConnectionStrings:DefaultConnection`.
- On startup, the app calls `Database.EnsureCreated()` so the initial schema is created automatically for local development.

## Realtime presence

- SignalR hub: `/hubs/presence?deviceId={deviceId}`
- When the connection succeeds, the server emits `connection:ready`.
- When the client disconnects, the server removes the connection from the in-memory tracker.
- Presence check API: `GET /api/realtime/presence?deviceId={deviceId}`
