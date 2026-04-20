using BackEndGame.Domain.Entities;
using BackEndGame.Domain.Packets;

namespace BackEndGame.Game
{
    /// <summary>
    /// Represents one fully isolated match session.
    /// Each match owns its own physics simulation (BEPU) and navmesh (DotRecast)
    /// so matches never share or interfere with each other's state.
    /// </summary>
    public class Match : IDisposable
    {
        public Guid MatchId { get; }
        public MatchResult LastResult { get; private set; } = MatchResult.Ongoing;

        // BEPUphysics Simulation instance — one per match.
        // Replace `object` with the concrete BEPU Simulation type when the package is added.
        private object? _physics;

        // DotRecast NavMeshQuery instance — one per match.
        // Replace `object` with the concrete DotRecast type when the package is added.
        private object? _navMesh;

        private readonly List<PlayerAgent> _players;
        private readonly List<EnemyAgent> _enemies;

        // Thread-safe queue: NetworkService pushes InputPackets in, Tick() drains them.
        private readonly Queue<InputPacket> _inputQueue;
        private readonly object _inputLock = new();

        private uint _tick;
        private DateTime _matchStartTime;

        private const float MoveSpeed = 5f;
        private const float SprintMultiplier = 1.6f;
        private const float JumpImpulse = 4f;
        private const float AttackRange = 2.5f;
        private const float EnemyMoveSpeed = 2.5f;
        private const float EnemyMeleeDamagePerSecond = 20f;
        private const float WeaponDamage = 25f;
        private const float TickInterval = 1f / 64f;
        private const float MaxMatchDurationMinutes = 15f;
        private const int DefaultEnemyCount = 5;
        private const float DefaultPlayerHp = 100f;
        private const float DefaultEnemyHp = 80f;

        public Match(Guid matchId, List<string> playerDeviceIds)
        {
            MatchId = matchId;
            _players = new List<PlayerAgent>();
            _enemies = new List<EnemyAgent>();
            _inputQueue = new Queue<InputPacket>();

            byte id = 0;
            foreach (var deviceId in playerDeviceIds)
            {
                _players.Add(new PlayerAgent
                {
                    PlayerId = id++,
                    DeviceId = deviceId,
                    Hp = DefaultPlayerHp,
                    IsAlive = true
                });
            }
        }

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        /// <summary>
        /// Initialises the match world:
        ///   1. Create a new BEPU Simulation with gravity and collision filters.
        ///   2. Parse the map .obj file → add static mesh bodies to BEPU (walls, floor).
        ///   3. Build the DotRecast navmesh from the same geometry for enemy pathfinding.
        ///   4. Spawn capsule bodies for each player at their start positions.
        ///   5. Spawn capsule/box bodies for each enemy at predefined spawn points.
        /// Called once by MatchManager.CreateMatch() before the loop task is started.
        /// </summary>
        public void Start()
        {
            _matchStartTime = DateTime.UtcNow;
            _tick = 0;
            LastResult = MatchResult.Ongoing;

            // TODO: _physics = Simulation.Create(...) with gravity (0, -9.8, 0) and collision filters
            // TODO: Load map .obj → parse vertices/indices → add StaticMesh body to _physics
            // TODO: _navMesh = new NavMeshQuery(...) built from same .obj geometry

            for (int i = 0; i < _players.Count; i++)
            {
                var player = _players[i];
                player.PosX = i * 2f;
                player.PosY = 1f;
                player.PosZ = 0f;
                player.RotYaw = 0f;
                player.Hp = DefaultPlayerHp;
                player.IsAlive = true;
                // TODO: player.PhysicsBodyHandle = _physics.Bodies.Add(CapsuleShape at spawn pos)
            }

            _enemies.Clear();
            for (byte i = 0; i < DefaultEnemyCount; i++)
            {
                var enemy = new EnemyAgent
                {
                    EnemyId = i,
                    Hp = DefaultEnemyHp,
                    IsAlive = true,
                    AnimState = 0,
                    PosX = i * 3f,
                    PosY = 1f,
                    PosZ = 20f
                };
                // TODO: enemy.PhysicsBodyHandle = _physics.Bodies.Add(CapsuleShape at spawn pos)
                _enemies.Add(enemy);
            }
        }

        /// <summary>
        /// Cleans up all resources owned by this match:
        ///   1. Dispose BEPU Simulation (frees native memory).
        ///   2. Dispose DotRecast navmesh.
        ///   3. Clear player and enemy lists.
        /// Called by MatchManager.DestroyMatch() after the loop task is cancelled.
        /// </summary>
        public void Stop()
        {
            // TODO: (_physics as IDisposable)?.Dispose()
            // TODO: (_navMesh as IDisposable)?.Dispose()
            _physics = null;
            _navMesh = null;
            _players.Clear();
            _enemies.Clear();
        }

        // ─── Per-tick entry point ─────────────────────────────────────────────────

        /// <summary>
        /// Runs one full simulation step. Called at 64 Hz by GameLoopService.
        /// Execution order is deterministic and must not change:
        ///   1. DequeueAllInputs
        ///   2. ProcessPlayerInputs
        ///   3. ProcessWeaponFiring
        ///   4. TickEnemyAI
        ///   5. StepPhysics
        ///   6. CheckWinLoseConditions
        ///   7. BuildGameStatePacket  (returns snapshot)
        ///   8. Caller (GameLoopService) broadcasts the snapshot via INetworkService.
        /// </summary>
        public GameStatePacket Tick()
        {
            var inputs = DequeueAllInputs();
            ProcessPlayerInputs(inputs);
            ProcessWeaponFiring(inputs);
            TickEnemyAI();
            StepPhysics();
            LastResult = CheckWinLoseConditions();
            _tick++;
            return BuildGameStatePacket();
        }

        // ─── Input ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Thread-safe: NetworkService calls this from its receive thread to push a
        /// client's InputPacket into the queue. Tick() drains the queue on the next step.
        /// Uses a lock to avoid concurrent write/read corruption on the Queue.
        /// </summary>
        public void EnqueueInput(InputPacket input)
        {
            lock (_inputLock)
            {
                _inputQueue.Enqueue(input);
            }
        }

        /// <summary>
        /// Drains all InputPackets that arrived since the last tick into a local list.
        /// If multiple packets arrived from the same player (lag burst), process them all
        /// in tick-order so movement stays smooth.
        /// </summary>
        private List<InputPacket> DequeueAllInputs()
        {
            var batch = new List<InputPacket>();
            lock (_inputLock)
            {
                while (_inputQueue.Count > 0)
                    batch.Add(_inputQueue.Dequeue());
            }
            return batch;
        }

        // ─── Tick sub-steps ───────────────────────────────────────────────────────

        /// <summary>
        /// For each InputPacket, update the corresponding PlayerAgent's BEPU capsule body:
        ///   - Convert MoveX/MoveZ + LookYaw into a world-space velocity vector.
        ///   - Apply sprint multiplier if IsSprinting.
        ///   - Apply jump impulse if IsJumping and player is grounded (BEPU contact test).
        ///   - Apply crouch by scaling capsule half-height.
        ///   - Update RotYaw / RotPitch on the PlayerAgent for state broadcasting.
        /// </summary>
        private void ProcessPlayerInputs(List<InputPacket> inputs)
        {
            foreach (var input in inputs)
            {
                var player = _players.Find(p => p.PlayerId == input.PlayerId);
                if (player == null || !player.IsAlive) continue;

                float yawRad = input.LookYaw * MathF.PI / 180f;
                float speed = input.IsSprinting ? MoveSpeed * SprintMultiplier : MoveSpeed;

                // Normalize diagonal movement so speed is consistent in all directions
                float moveLen = MathF.Sqrt(input.MoveX * input.MoveX + input.MoveZ * input.MoveZ);
                float scale = moveLen > 1f ? 1f / moveLen : 1f;
                float normX = input.MoveX * scale;
                float normZ = input.MoveZ * scale;

                // World-space velocity: forward=(sin,cos), right=(cos,-sin) relative to yaw
                // TODO: Apply velX/velZ to BEPU capsule LinearVelocity instead of direct position delta
                player.PosX += (MathF.Sin(yawRad) * normZ + MathF.Cos(yawRad) * normX) * speed * TickInterval;
                player.PosZ += (MathF.Cos(yawRad) * normZ - MathF.Sin(yawRad) * normX) * speed * TickInterval;

                // Jump: only when grounded (Y ≈ 1). TODO: replace with BEPU contact test.
                if (input.IsJumping && MathF.Abs(player.PosY - 1f) < 0.05f)
                    player.PosY += JumpImpulse * TickInterval;

                // Placeholder gravity until BEPU steps physics
                if (player.PosY > 1f)
                    player.PosY = MathF.Max(1f, player.PosY - 9.8f * TickInterval);

                player.RotYaw = input.LookYaw;
                player.RotPitch = Math.Clamp(input.LookPitch, -89f, 89f);
            }
        }

        /// <summary>
        /// For each player whose IsShooting == true:
        ///   1. Construct a ray from (posX, posY, posZ) in the LookYaw+LookPitch direction.
        ///   2. Call BEPU RayCast against all bodies in the simulation.
        ///   3. If the hit body belongs to an EnemyAgent → call TakeDamage() on that enemy.
        ///   4. If the hit body belongs to another PlayerAgent → call TakeDamage() on that player.
        ///   5. Apply hit feedback (recoil, muzzle flash anim state) — server-authoritative only.
        /// </summary>
        private void ProcessWeaponFiring(List<InputPacket> inputs)
        {
            foreach (var input in inputs)
            {
                if (!input.IsShooting) continue;

                var shooter = _players.Find(p => p.PlayerId == input.PlayerId);
                if (shooter == null || !shooter.IsAlive) continue;

                float yawRad = input.LookYaw * MathF.PI / 180f;
                float pitchRad = input.LookPitch * MathF.PI / 180f;

                // Unit direction vector from yaw + pitch
                float dirX = MathF.Sin(yawRad) * MathF.Cos(pitchRad);
                float dirY = -MathF.Sin(pitchRad);
                float dirZ = MathF.Cos(yawRad) * MathF.Cos(pitchRad);

                // TODO: Replace proximity check with BEPU RayCast(origin, direction, maxDistance)
                const float maxRange = 100f;
                const float hitRadius = 0.6f;

                foreach (var enemy in _enemies)
                {
                    if (!enemy.IsAlive) continue;
                    if (IsHitByRay(shooter.PosX, shooter.PosY, shooter.PosZ,
                                   dirX, dirY, dirZ,
                                   enemy.PosX, enemy.PosY, enemy.PosZ, maxRange, hitRadius))
                    {
                        TakeDamage(enemy, WeaponDamage);
                        break;
                    }
                }

                foreach (var target in _players)
                {
                    if (!target.IsAlive || target.PlayerId == shooter.PlayerId) continue;
                    if (IsHitByRay(shooter.PosX, shooter.PosY, shooter.PosZ,
                                   dirX, dirY, dirZ,
                                   target.PosX, target.PosY, target.PosZ, maxRange, hitRadius: 0.5f))
                    {
                        TakeDamage(target, WeaponDamage);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Updates every living EnemyAgent using DotRecast pathfinding:
        ///   1. Find the nearest living player as the chase target.
        ///   2. If enemy is within attack range → set AnimState = 2 (attack), deal melee damage.
        ///   3. Otherwise, request a DotRecast path to the target player's position.
        ///   4. Move the enemy's BEPU body along the path waypoints.
        ///   5. Update AnimState: 1 (patrol/move) while traveling, 0 (idle) if no path found.
        ///   6. If enemy is dead → set AnimState = 3, skip pathfinding.
        /// </summary>
        private void TickEnemyAI()
        {
            var livingPlayers = _players.FindAll(p => p.IsAlive);

            foreach (var enemy in _enemies)
            {
                if (!enemy.IsAlive)
                {
                    enemy.AnimState = 3;
                    continue;
                }

                if (livingPlayers.Count == 0)
                {
                    enemy.AnimState = 0;
                    continue;
                }

                // Find nearest living player
                PlayerAgent? nearest = null;
                float minDist = float.MaxValue;
                foreach (var p in livingPlayers)
                {
                    float dx = p.PosX - enemy.PosX;
                    float dz = p.PosZ - enemy.PosZ;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);
                    if (dist < minDist) { minDist = dist; nearest = p; }
                }

                if (nearest == null) continue;

                if (minDist <= AttackRange)
                {
                    enemy.AnimState = 2;
                    TakeDamage(nearest, EnemyMeleeDamagePerSecond * TickInterval);
                }
                else
                {
                    // TODO: Replace straight-line move with DotRecast navmesh path following
                    enemy.AnimState = 1;
                    float dx = nearest.PosX - enemy.PosX;
                    float dz = nearest.PosZ - enemy.PosZ;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);
                    if (dist > 0f)
                    {
                        enemy.PosX += dx / dist * EnemyMoveSpeed * TickInterval;
                        enemy.PosZ += dz / dist * EnemyMoveSpeed * TickInterval;
                        enemy.RotYaw = MathF.Atan2(dx, dz) * 180f / MathF.PI;
                    }
                }
            }
        }

        /// <summary>
        /// Advances the BEPU Simulation by one fixed timestep (1/64 s).
        /// BEPU resolves all collisions, applies gravity, and updates body positions internally.
        /// After this call, all PhysicsBodyHandle positions reflect the new world state.
        /// </summary>
        private void StepPhysics()
        {
            // TODO: _physics.Timestep(TickInterval) — resolves collisions, gravity, updates all body positions
        }

        /// <summary>
        /// Checks end-game conditions after physics has settled:
        ///   - Win:  all EnemyAgents have IsAlive == false.
        ///   - Lose: all PlayerAgents have IsAlive == false.
        ///   - Timeout: elapsed match time exceeds the configured max duration.
        /// Returns a MatchResult enum (Ongoing / Win / Lose / Timeout).
        /// If not Ongoing, signals GameLoopService to call MatchManager.DestroyMatch().
        /// </summary>
        private MatchResult CheckWinLoseConditions()
        {
            if (DateTime.UtcNow - _matchStartTime >= TimeSpan.FromMinutes(MaxMatchDurationMinutes))
                return MatchResult.Timeout;

            if (_enemies.TrueForAll(e => !e.IsAlive))
                return MatchResult.Win;

            if (_players.TrueForAll(p => !p.IsAlive))
                return MatchResult.Lose;

            return MatchResult.Ongoing;
        }

        /// <summary>
        /// Snapshots the current world state into a GameStatePacket:
        ///   - Reads each PlayerAgent's BEPU body position and copies to PlayerState.
        ///   - Reads each EnemyAgent's BEPU body position and AnimState, copies to EnemyState.
        ///   - Stamps the current server tick counter.
        /// This packet is returned from Tick() and broadcast to all clients.
        /// </summary>
        private GameStatePacket BuildGameStatePacket()
        {
            var playerStates = new PlayerState[_players.Count];
            for (int i = 0; i < _players.Count; i++)
            {
                var p = _players[i];
                // TODO: Read PosX/Y/Z from BEPU body handle once physics is integrated
                playerStates[i] = new PlayerState
                {
                    PlayerId = p.PlayerId,
                    PosX = p.PosX,
                    PosY = p.PosY,
                    PosZ = p.PosZ,
                    RotYaw = p.RotYaw,
                    Hp = p.Hp,
                    IsAlive = p.IsAlive
                };
            }

            var enemyStates = new EnemyState[_enemies.Count];
            for (int i = 0; i < _enemies.Count; i++)
            {
                var e = _enemies[i];
                // TODO: Read PosX/Y/Z from BEPU body handle once physics is integrated
                enemyStates[i] = new EnemyState
                {
                    EnemyId = e.EnemyId,
                    PosX = e.PosX,
                    PosY = e.PosY,
                    PosZ = e.PosZ,
                    RotYaw = e.RotYaw,
                    Hp = e.Hp,
                    IsAlive = e.IsAlive,
                    AnimState = e.AnimState
                };
            }

            return new GameStatePacket
            {
                Tick = _tick,
                Players = playerStates,
                Enemies = enemyStates
            };
        }

        // ─── Damage ───────────────────────────────────────────────────────────────

        /// <summary>
        /// Applies damage to a PlayerAgent:
        ///   - Subtracts amount from Hp (clamped to 0).
        ///   - If Hp reaches 0, sets IsAlive = false and removes the BEPU body from the simulation.
        /// </summary>
        private void TakeDamage(PlayerAgent player, float amount)
        {
            player.Hp = MathF.Max(0f, player.Hp - amount);
            if (player.Hp <= 0f && player.IsAlive)
            {
                player.IsAlive = false;
                // TODO: _physics.Bodies.Remove(player.PhysicsBodyHandle)
            }
        }

        /// <summary>
        /// Applies damage to an EnemyAgent:
        ///   - Subtracts amount from Hp (clamped to 0).
        ///   - If Hp reaches 0, sets IsAlive = false, AnimState = 3, removes BEPU body.
        /// </summary>
        private void TakeDamage(EnemyAgent enemy, float amount)
        {
            enemy.Hp = MathF.Max(0f, enemy.Hp - amount);
            if (enemy.Hp <= 0f && enemy.IsAlive)
            {
                enemy.IsAlive = false;
                enemy.AnimState = 3;
                // TODO: _physics.Bodies.Remove(enemy.PhysicsBodyHandle)
            }
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        // Projects target onto the ray and checks if perpendicular distance is within hitRadius.
        // Requires a normalised direction vector (dx, dy, dz).
        private static bool IsHitByRay(
            float ox, float oy, float oz,
            float dx, float dy, float dz,
            float tx, float ty, float tz,
            float maxRange, float hitRadius)
        {
            float ax = tx - ox, ay = ty - oy, az = tz - oz;
            float t = ax * dx + ay * dy + az * dz;
            if (t < 0f || t > maxRange) return false;
            float perpX = ax - t * dx, perpY = ay - t * dy, perpZ = az - t * dz;
            return perpX * perpX + perpY * perpY + perpZ * perpZ <= hitRadius * hitRadius;
        }

        // ─── IDisposable ──────────────────────────────────────────────────────────

        public void Dispose() => Stop();
    }

    public enum MatchResult
    {
        Ongoing,
        Win,
        Lose,
        Timeout
    }
}
