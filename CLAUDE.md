# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build BackEndGame.csproj

# Run (development, opens Swagger at http://localhost:5039/swagger)
dotnet run --launch-profile http

# Run (HTTPS)
dotnet run --launch-profile https
```

No test project exists yet. Database schema is auto-created via `Database.EnsureCreated()` on startup — no migration commands needed.

## Architecture

This is a **multiplayer FPS game backend** using an **authoritative server** model. The Unity client is view-only (sends inputs, renders received state). All game logic — movement, physics, shooting, AI, win/lose detection — runs on the server.

### Layers

| Layer | Location | Responsibility |
|-------|----------|----------------|
| REST API | `Features/User`, `Features/Friend`, `Features/Realtime` | User login (device-based), friend requests, online presence |
| SignalR Hubs | `Features/Network/GameHub`, `Features/Realtime/PresenceHub` | In-game packet traffic and presence tracking |
| Match System | `Game/Match.cs`, `Game/MatchManager.cs` | Isolated per-match game loop, physics, pathfinding |
| Game Loop Service | `Features/GameLoop/GameLoopService.cs` | `BackgroundService` orchestrating match lifecycle |
| Network Service | `Features/Network/SignalRNetworkService.cs` | SignalR group management, device↔connection mapping |
| Domain | `Domain/Entities`, `Domain/Packets` | Entities and packet structs (no business logic) |
| Infrastructure | `Infrastructure/` | EF Core `AppDbContext`, repository implementations |

### Match Lifecycle & Threading

1. `MatchManager.CreateMatch()` initializes 10 players, BEPU physics, DotRecast navmesh
2. `GameLoopService` calls `StartMatchAsync()` → notifies players via SignalR group `match:{matchId}`
3. Match runs a **64 Hz tick loop** (`await Task.Delay(15)`) — never use `Thread.Sleep()`
4. Each tick: dequeue `InputPacket`s → `match.Tick()` → BEPU step → `BroadcastGameStateAsync()` to group
5. On terminal condition: `NotifyMatchEndedAsync()` → `DestroyMatchAsync()`

`InputPacket` queue is guarded by `lock(_inputLock)` (network thread writes, game loop thread reads). Match tracking and device↔connection maps use `ConcurrentDictionary`.

### SignalR Hubs

**GameHub** (`/hubs/game?deviceId={deviceId}`):
- Client → Server: `SendInput(matchId, inputPacket)` at ~64 Hz
- Server → Client: `game:state` (64 Hz), `match:found`, `match:started`, `match:ended`

**PresenceHub** (`/hubs/presence?deviceId={deviceId}`):
- Server → Client: `connection:ready` on connect
- Client → Server: `GetConnectionState()`

### Database

PostgreSQL via EF Core (Npgsql). Connection string in `appsettings.json` (`ConnectionStrings:DefaultConnection`). Three persisted tables: `Users`, `FriendRequests`, `Friendships`. All in-match state (`PlayerAgent`, `EnemyAgent`, physics bodies) is ephemeral.

`User` accounts are device-based: a new `User` is auto-created with `UserName = "Guest_{6-char-guid}"` on first login. `Friendship` pairs store `UserOneId < UserTwoId` to enforce uniqueness.

### Key Packets

- **`InputPacket`**: Tick, PlayerId, MoveX/Z, LookYaw/Pitch, IsShooting, IsJumping, IsCrouching, IsSprinting
- **`GameStatePacket`**: Tick + `PlayerState[]` (PlayerId, TeamId, PosX/Y/Z, RotYaw, Hp, IsAlive)

### Physics & Pathfinding

- **BEPUphysics v2**: One `Simulation` per active match. Map collision loaded from `.obj` files exported from Unity → BEPU `StaticMesh`.
- **DotRecast**: One `NavMeshQuery` per match for `EnemyAgent` AI pathfinding.

### Serialization Note

Current transport is JSON. For production, switch to **MessagePack** (binary) to reduce 64 Hz packet overhead.