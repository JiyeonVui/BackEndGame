# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build
dotnet build BackEndGame.csproj

# Run (development — Swagger at http://localhost:5039/swagger)
dotnet run --launch-profile http

# Run (HTTPS)
dotnet run --launch-profile https
```

Target: **.NET 8**. No test project exists yet. Database schema is auto-created via `Database.EnsureCreated()` on startup — no migration commands needed.

## Architecture

This is a **multiplayer FPS game backend** using an **authoritative server** model. The Unity client is view-only (sends inputs, renders received state). All game logic — movement, physics, shooting, win/lose detection — runs on the server.

Source lives entirely under `src/`.

### Layers

| Layer | Location | Responsibility |
|-------|----------|----------------|
| REST API | `Features/User`, `Features/Friend`, `Features/Realtime` | Device-based login, friend requests, online presence |
| SignalR Hubs | `Features/Network/GameHub`, `Features/Realtime/PresenceHub` | In-game packet traffic and presence tracking |
| Game Loop Service | `Features/GameLoop/GameLoopService.cs` | `BackgroundService`; exposes `StartMatchAsync` / `StopMatchAsync` for the matchmaking REST layer |
| Network Service | `Features/Network/SignalRNetworkService.cs` | SignalR group management, deviceId↔connectionId mapping |
| Match System | `Game/Match.cs`, `Game/MatchManager.cs` | Isolated per-match game loop, physics stubs, win/lose logic |
| Domain | `Domain/Entities`, `Domain/Packets` | Plain entities and packet classes (no business logic). Entities: `User`, `FriendRequest`, `Friendship`, `PlayerAgent`, `EnemyAgent` (stub). |
| Infrastructure | `Infrastructure/` | EF Core `AppDbContext`, repository implementations |

### REST Endpoints

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/users/login` | Upsert user by `deviceId`; optional `userName` field sets/updates display name; auto-generates `Guest_{6-char}` on first call if omitted |
| GET | `/api/users/search?deviceId=` | Look up a user by device ID |
| POST | `/api/friends/requests` | Send a friend request (`senderDeviceId`, `receiverDeviceId`) |
| GET | `/api/friends/requests/notifications?deviceId=` | Pending incoming friend requests |
| POST | `/api/friends/requests/{requestId}/respond` | Accept or decline a request |
| GET | `/api/friends?deviceId=` | List confirmed friends |
| GET | `/api/realtime/presence?deviceId=` | Online status + active connection count |
| POST | `/api/matchmaking/find` | **Stub only** — 5 s delay, creates solo test match |

### Match Format

Each match is **5v5** (team 0 vs team 1, `TeamId` 0 or 1). `Match` is constructed with two `List<string>` of `deviceId`s; players get sequential `PlayerId` bytes (0–4 for team 0, 5–9 for team 1).

`MatchResult` enum: `Ongoing`, `Team0Win`, `Team1Win`, `Timeout` (15-minute cap).

### Match Lifecycle & Threading

1. REST controller calls `GameLoopService.StartMatchAsync(team0Ids, team1Ids)`
2. `MatchManager.CreateMatch()` initialises players, fires off a `Task.Run` for the 64 Hz loop
3. `NotifyMatchFoundAsync` → clients see "match:found" (Home → Loading), payload: `{ matchId, teamId, playerId }`
4. `NotifyMatchStartedAsync` → clients join SignalR group `match:{matchId}`, see "match:started" (Loading → Game)
5. Each tick (15 ms): dequeue `InputPacket`s → `Match.Tick()` → `BroadcastGameStateAsync("game:state")`
6. Terminal condition: `NotifyMatchEndedAsync` → `DestroyMatchAsync` (cancels loop, calls `Match.Stop()`)

**Threading rules:**
- Always use `await Task.Delay()`, never `Thread.Sleep()` — releasing threads lets the pool handle 30–60 concurrent matches on an 8-core VM.
- `InputPacket` queue is guarded by `lock(_inputLock)` (network thread writes, game loop thread reads).
- Match tracking and device↔connection maps use `ConcurrentDictionary`.
- `_matchConnections` (group membership) is guarded by `_matchConnectionsLock` because `HashSet` is not thread-safe.

### SignalR Hubs

**GameHub** (`/hubs/game?deviceId={deviceId}`):
- Client → Server: `SendInput(matchId, inputPacket)` at ~64 Hz
- Server → Client: `game:state` (64 Hz), `match:found`, `match:started`, `match:ended`
- `OnConnectedAsync` registers deviceId↔connectionId; `OnDisconnectedAsync` destroys the match if no connections remain.

**PresenceHub** (`/hubs/presence?deviceId={deviceId}`):
- Server → Client: `connection:ready` on connect
- Client → Server: `GetConnectionState()`

### Dependency Injection Notes

`SignalRNetworkService` is registered **twice** in `Program.cs`:
```csharp
builder.Services.AddSingleton<SignalRNetworkService>();                          // concrete type (GameHub needs it)
builder.Services.AddSingleton<INetworkService>(sp => sp.GetRequiredService<SignalRNetworkService>()); // interface
```
`MatchManager` is registered as `AddSingleton<IMatchManager, MatchManager>()`. Game services (`SignalRNetworkService`, `MatchManager`, `GameLoopService`) are all **singletons** — state must persist for the server lifetime. REST services (`UserService`, `FriendService`, repositories) are **scoped**.

`AddJsonProtocol` in `Program.cs` sets three options required for Unity ↔ server compatibility:
- `IncludeFields = true` — serialises public fields on packet classes
- `PropertyNamingPolicy = null` — keeps PascalCase to match Unity field names
- `PropertyNameCaseInsensitive = true` — accepts PascalCase input from Unity on deserialise

`GameLoopService` is registered twice (same pattern as `SignalRNetworkService`):
```csharp
builder.Services.AddSingleton<GameLoopService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<GameLoopService>());
```

### Database

PostgreSQL via EF Core (Npgsql). Connection string: `appsettings.json` → `ConnectionStrings:DefaultConnection`. Three persisted tables: `Users`, `FriendRequests`, `Friendships`. All in-match state is ephemeral.

`User` accounts are device-based: auto-created with `UserName = "Guest_{6-char-guid}"` on first login. `Friendship` pairs store `UserOneId < UserTwoId` to enforce uniqueness.

### Key Packets

Both packet types are **classes** with `{ get; set; }` properties (not structs).

- **`InputPacket`**: `Tick`, `PlayerId`, `MoveX/Z`, `LookYaw/Pitch`, `IsShooting`, `IsJumping`, `IsCrouching`, `IsSprinting`
- **`GameStatePacket`**: `Tick` + `PlayerState[]` (`PlayerId`, `TeamId`, `PosX/Y/Z`, `RotYaw`, `Hp`, `IsAlive`)

**Tick semantics**: `InputPacket.Tick` is the client's local counter (used for client-side reconciliation). `GameStatePacket.Tick` is the server's `_tick++` counter (increments every 15 ms in `Match.Tick()`). The two are independent — they will not be equal.

`PlayerAgent` stores both `RotYaw` and `RotPitch`, but `PlayerState` (the broadcast snapshot) only includes `RotYaw`. `RotPitch` is applied server-side to weapon ray direction but is not sent to other clients.

### Physics & Pathfinding (Current State)

**BEPUphysics v2** (`BEPUphysics` NuGet 2.4.0) is referenced but integration is in progress — `_physics` is typed as `object?` and most BEPU calls are `// TODO` stubs. The kinematics math (movement, gravity, ray/hit detection) runs as direct position arithmetic until BEPU is wired in.

**DotRecast** is planned but not yet in the `.csproj`. Enemy AI pathfinding stubs reference it in comments.

### Matchmaking Stubs

There are **two** separate testing entry points for starting a match — both are stubs:

1. `POST /api/matchmaking/find` (`MatchmakingController`) — HTTP REST stub, waits 5 s, creates solo match.
2. `GameHub.FindGame(deviceId)` — SignalR Hub method, same behaviour (5 s delay, solo match). Clients can invoke either.

Real matchmaking (accumulating a queue of 10 players) is not yet implemented. When implementing it, consolidate these two entry points.

### Presence Tracking

`InMemoryRealtimeConnectionTracker` (singleton) — stores `deviceId → connection count` for the `PresenceHub`. Separate from `SignalRNetworkService`'s game connection maps. `PresenceHub` is only for "is online" status; game packet traffic goes through `GameHub`.

### Other Notes

- `UserService.NormalizeDeviceId()` trims whitespace before all DB lookups and inserts — `"abc"` and `" abc "` map to the same user.
- `GameLoopService.ExecuteAsync()` runs a lightweight monitor loop every 5 s that logs active match count. Crash detection/restart is not yet implemented.
- `appsettings.Development.json` has `"BackEndGame.Features.Network.GameHub": "Debug"` to surface `LogDebug` calls (e.g., `SendInput OK` on the success path). The default `Information` level suppresses these.

### Known Implementation Gaps

These are intentional stubs marked with `// TODO` throughout the codebase:

| Area | Status |
|------|--------|
| `Match._physics` | Typed as `object?`; all BEPU `Simulation` calls are `// TODO` stubs |
| Map loading | `.obj` → BEPU `StaticMesh` not yet implemented in `Match.Start()` |
| Win/lose check | `CheckWinLoseConditions()` exists but is **commented out** in `Match.Tick()` |
| Hit detection | Proximity math placeholder — no actual BEPU `RayCast` yet |
| PlayFab stats | `DestroyMatchAsync` has a `// TODO` for reporting kill/death data |
| DotRecast | Package not yet added to `.csproj`; enemy AI is unimplemented |

### Serialization Note

Current transport is JSON. Switch to **MessagePack** (binary) for production to reduce 64 Hz packet overhead.