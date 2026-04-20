# Game Server Architecture — Design Decisions

> Summary of architecture discussion for the multiplayer FPS game backend.
> Reference project: Unity-Azure-Cloud-Game (client-side FPS with PlayFab)

---

## Context

Building a multiplayer FPS game where:
- **Unity** = client only (rendering, input, view)
- **ASP.NET (.NET)** = authoritative server (all game logic)
- Multiple matches run concurrently (10 players per match)

---

## Core Principle: Authoritative Server

All game logic runs on the server. Client is dumb.

```
CLIENT (Unity)                    SERVER (ASP.NET)
──────────────                    ────────────────
Read input                  →     Receive InputPacket
Send InputPacket            →     Process movement (BEPU)
                                  Process shooting (BEPU raycast)
                                  Process damage (Health system)
                                  Tick enemy AI (DotRecast)
                                  Build GameStatePacket
Receive GameStatePacket     ←     Broadcast state
Render positions/HP/anims         (no logic here)
```

---

## Technology Stack

| Layer            | Technology                          |
|------------------|-------------------------------------|
| HTTP / Auth      | ASP.NET Web API                     |
| Game loop        | .NET BackgroundService              |
| Transport        | UDP or SignalR WebSocket            |
| Physics          | BEPUphysics v2 (pure C#, no Unity)  |
| Pathfinding      | DotRecast (C# port of Recast/Detour)|
| Cloud / Auth     | Azure PlayFab                       |
| Client           | Unity (view + input only)           |

---

## Packet Design

### Client → Server (every tick ~64Hz)
```csharp
struct InputPacket
{
    uint  tick;
    byte  playerId;
    float moveX, moveZ;
    float lookYaw, lookPitch;
    bool  isShooting;
    bool  isJumping;
    bool  isCrouching;
    bool  isSprinting;
}
```

### Server → All Clients (every tick)
```csharp
struct GameStatePacket
{
    uint          tick;
    PlayerState[] players;
    EnemyState[]  enemies;
}

struct PlayerState
{
    byte  playerId;
    float posX, posY, posZ;
    float rotYaw;
    float hp;
    bool  isAlive;
}

struct EnemyState
{
    byte  enemyId;
    float posX, posY, posZ;
    float rotYaw;
    float hp;
    bool  isAlive;
    byte  animState;  // 0=idle, 1=patrol, 2=attack, 3=dead
}
```

---

## Overall Server Architecture

```
ASP.NET Server
  ├── REST Controllers          → auth, matchmaking, leaderboard (PlayFab)
  ├── NetworkService            → receive InputPacket, route to match
  └── GameLoopService           → BackgroundService, owns MatchManager
        └── MatchManager
              ├── Match_001     → isolated Task + BEPU + DotRecast
              ├── Match_002     → isolated Task + BEPU + DotRecast
              └── Match_N       → isolated Task + BEPU + DotRecast
```

---

## Multi-Match Design

Each match is fully isolated:

```csharp
class Match
{
    Guid               matchId;
    Simulation         physics;      // BEPUphysics — one per match
    NavMeshQuery       navMesh;      // DotRecast — one per match
    List<PlayerAgent>  players;
    List<EnemyAgent>   enemies;
    Queue<InputPacket> inputQueue;

    void Start();
    void Stop();
    void Tick();
    void EnqueueInput(InputPacket);
    GameStatePacket BuildState();
}

class MatchManager
{
    Dictionary<Guid, Match> activeMatches;

    Match CreateMatch(List<string> playerIds);
    void  DestroyMatch(Guid matchId);
    Match GetMatch(Guid matchId);
    void  RouteInputToMatch(Guid matchId, InputPacket input);
}
```

---

## Thread Pool Model

**Rule: use `await Task.Delay()` NOT `Thread.Sleep()`**

```csharp
// Each match loop — releases thread during wait
async Task MatchLoop(Match match, CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        var tickStart = DateTime.UtcNow;

        match.Tick();                         // ~1ms work

        var elapsed  = DateTime.UtcNow - tickStart;
        var waitTime = TICK_INTERVAL - elapsed;

        await Task.Delay(waitTime, ct);       // releases thread back to pool
    }
}
```

```
Thread.Sleep()      → holds thread blocked for 14ms   ✗  (max 8 matches on 8 cores)
await Task.Delay()  → releases thread during wait     ✓  (30-60 matches on 8 cores)
```

### Capacity estimate (8-core server)
```
Each match tick  ≈ 1ms
Tick interval    = 15ms (64Hz)
Free per tick    = 14ms per thread
8 threads × 14ms = 112ms available per window
→ ~30-60 concurrent matches on a single 8-core VM
```

---

## Match Lifecycle

```
PlayFab Matchmaking finds 10 players
  └─ MatchManager.CreateMatch(playerIds)
       ├─ new Match(id, players)
       ├─ Load map .obj → BEPU static mesh
       ├─ Load navmesh  → DotRecast
       ├─ Spawn player bodies
       ├─ Spawn enemy bodies
       └─ Task.Run(() => MatchLoop(match, cts.Token))

Match runs at 64Hz...
  ├─ Win  (all enemies dead)   ─┐
  ├─ Lose (all players dead)   ─┼─ MatchManager.DestroyMatch(id)
  └─ Timeout                   ─┘       ├─ cts.Cancel()
                                         ├─ match.Dispose()  // free BEPU + navmesh
                                         └─ report stats to PlayFab
```

---

## Map Collision Workflow

```
Unity Editor
  └─ Export collision mesh (invisible geometry) → .obj file
  └─ Bundle with server deployment on Azure

Server startup (per match)
  └─ Parse .obj → BEPUphysics StaticMesh (walls, floor, obstacles)
  └─ Parse navmesh → DotRecast (enemy pathfinding)
  └─ Server now has same collision world as Unity client shows visually
```

---

## Server Tick Flow (per match, per tick)

```
MatchLoop.Tick()
  ├── DequeueAllInputs()          // collect all InputPackets this tick
  ├── ProcessPlayerInputs()       // apply move/look to BEPU capsule bodies
  ├── ProcessWeaponFiring()       // if shooting → BEPU RayCast → TakeDamage()
  ├── TickEnemyAI()               // DotRecast pathfind → move → attack
  ├── StepPhysics()               // BEPU Simulation.Timestep()
  ├── CheckWinLoseConditions()
  ├── BuildGameStatePacket()      // snapshot all positions + HP
  └── BroadcastToAllPlayers()     // send GameStatePacket to 10 clients
```

---

## What NOT to put in Unity client

| System           | Old (wrong)  | New (correct)  |
|------------------|--------------|----------------|
| TakeDamage()     | Client       | Server only    |
| Raycast / hits   | Client       | Server only    |
| HP calculation   | Client       | Server only    |
| Enemy AI         | Client       | Server only    |
| Physics          | Client       | Server only    |
| Player movement  | Client       | Server only    |
| Rendering        | Client       | Client (ok)    |
| Animations       | Client       | Client (ok)    |
| Camera / look    | Client       | Client (ok)    |
| Input reading    | Client       | Client (ok)    |

---

## Next Steps (implementation order)

1. Define `InputPacket` and `GameStatePacket` structs (shared contract)
2. Setup `BEPUphysics v2` NuGet package
3. Setup `DotRecast` NuGet package
4. Implement `Match` class with isolated BEPU Simulation
5. Implement `MatchManager` with `CreateMatch` / `DestroyMatch`
6. Implement `GameLoopService` as BackgroundService
7. Implement `NetworkService` (UDP or SignalR) for packet I/O
8. Implement `Match.Tick()` full loop
9. Strip Unity client of all logic — input only + state renderer
10. Export map collision from Unity → load into BEPU on server
