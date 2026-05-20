using BackEndGame.Domain.Entities;
using BackEndGame.Domain.Packets;

namespace BackEndGame.Game
{
    /// <summary>
    /// Represents one fully isolated match session.
    /// Two teams of 5 players fight each other — all players are client-controlled.
    /// Each match owns its own physics simulation (BEPU) so matches never interfere.
    /// </summary>
    public class Match : IDisposable
    {
        public Guid MatchId { get; }
        public MatchResult LastResult { get; private set; } = MatchResult.Ongoing;

        // BEPUphysics Simulation instance — one per match.
        // Replace `object` with the concrete BEPU Simulation type when the package is added.
        private object? _physics;

        private readonly List<PlayerAgent> _players;

        // Thread-safe queue: NetworkService pushes InputPackets in, Tick() drains them.
        private readonly Queue<InputPacket> _inputQueue;
        private readonly object _inputLock = new();

        private uint _tick;
        private DateTime _matchStartTime;

        private const float MoveSpeed = 5f;
        private const float SprintMultiplier = 1.6f;
        private const float JumpImpulse = 4f;
        private const float WeaponDamage = 25f;
        private const float TickInterval = 1f / 64f;
        private const float MaxMatchDurationMinutes = 15f;
        private const float DefaultPlayerHp = 100f;


        public Match(Guid matchId, List<string> team0DeviceIds, List<string> team1DeviceIds)
        {
            MatchId = matchId;
            _players = new List<PlayerAgent>();
            _inputQueue = new Queue<InputPacket>();

            byte id = 0;
            foreach (var deviceId in team0DeviceIds)
                _players.Add(new PlayerAgent { PlayerId = id++, TeamId = 0, DeviceId = deviceId, Hp = DefaultPlayerHp, IsAlive = true });

            foreach (var deviceId in team1DeviceIds)
                _players.Add(new PlayerAgent { PlayerId = id++, TeamId = 1, DeviceId = deviceId, Hp = DefaultPlayerHp, IsAlive = true });
        }

        // ─── Lifecycle ────────────────────────────────────────────────────────────

        /// <summary>
        /// Initialises the match world:
        ///   1. Create a new BEPU Simulation with gravity and collision filters.
        ///   2. Parse the map .obj file → add static mesh bodies to BEPU (walls, floor).
        ///   3. Spawn capsule bodies for each player at their team's start positions.
        /// Called once by MatchManager.CreateMatch() before the loop task is started.
        /// </summary>
        public void Start()
        {
            _matchStartTime = DateTime.UtcNow;
            _tick = 0;
            LastResult = MatchResult.Ongoing;

            // TODO: _physics = Simulation.Create(...) with gravity (0, -9.8, 0)
            // TODO: Load map .obj → add StaticMesh body to _physics

            // Team 0 spawns at Z=0, Team 1 spawns at Z=20, facing each other
            var team0 = _players.FindAll(p => p.TeamId == 0);
            var team1 = _players.FindAll(p => p.TeamId == 1);

            for (int i = 0; i < team0.Count; i++)
            {
                team0[i].PosX = (i - team0.Count / 2f) * 2f;
                team0[i].PosY = 1f;
                team0[i].PosZ = 0f;
                team0[i].RotYaw = 0f;
                team0[i].Hp = DefaultPlayerHp;
                team0[i].IsAlive = true;
                // TODO: team0[i].PhysicsBodyHandle = _physics.Bodies.Add(CapsuleShape at spawn pos)
            }

            for (int i = 0; i < team1.Count; i++)
            {
                team1[i].PosX = (i - team1.Count / 2f) * 2f;
                team1[i].PosY = 1f;
                team1[i].PosZ = 20f;
                team1[i].RotYaw = 180f;
                team1[i].Hp = DefaultPlayerHp;
                team1[i].IsAlive = true;
                // TODO: team1[i].PhysicsBodyHandle = _physics.Bodies.Add(CapsuleShape at spawn pos)
            }
        }

        /// <summary>
        /// Cleans up all resources owned by this match:
        ///   1. Dispose BEPU Simulation (frees native memory).
        ///   2. Clear player list.
        /// Called by MatchManager.DestroyMatch() after the loop task is cancelled.
        /// </summary>
        public void Stop()
        {
            // TODO: (_physics as IDisposable)?.Dispose()
            _physics = null;
            _players.Clear();
        }

        // ─── Per-tick entry point ─────────────────────────────────────────────────

        /// <summary>
        /// Runs one full simulation step. Called at 64 Hz by MatchManager.
        /// Execution order is deterministic and must not change:
        ///   1. DequeueAllInputs
        ///   2. ProcessPlayerInputs
        ///   3. ProcessWeaponFiring
        ///   4. StepPhysics
        ///   5. CheckWinLoseConditions
        ///   6. BuildGameStatePacket  (returns snapshot)
        /// </summary>
        public GameStatePacket Tick()
        {
            var inputs = DequeueAllInputs();
            ProcessPlayerInputs(inputs);
            ProcessWeaponFiring(inputs);
            StepPhysics();
            // LastResult = CheckWinLoseConditions(); // TODO: re-enable after flow testing
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

                // Normalize diagonal movement so speed stays consistent in all directions
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
        ///   1. Construct a ray from their position in the LookYaw+LookPitch direction.
        ///   2. Call BEPU RayCast against all bodies in the simulation.
        ///   3. If the hit body belongs to a player on the OPPOSITE team → TakeDamage().
        ///   Friendly fire is not applied — hits on same-team players are ignored.
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
                const float hitRadius = 0.5f;

                foreach (var target in _players)
                {
                    if (!target.IsAlive || target.TeamId == shooter.TeamId) continue;

                    if (IsHitByRay(shooter.PosX, shooter.PosY, shooter.PosZ,
                                   dirX, dirY, dirZ,
                                   target.PosX, target.PosY, target.PosZ, maxRange, hitRadius))
                    {
                        TakeDamage(target, WeaponDamage);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Advances the BEPU Simulation by one fixed timestep (1/64 s).
        /// BEPU resolves all collisions, applies gravity, and updates body positions internally.
        /// </summary>
        private void StepPhysics()
        {
            // TODO: _physics.Timestep(TickInterval)
        }

        /// <summary>
        /// Checks end-game conditions after physics has settled:
        ///   - Team0Win: all Team 1 players have IsAlive == false.
        ///   - Team1Win: all Team 0 players have IsAlive == false.
        ///   - Timeout:  elapsed match time exceeds MaxMatchDurationMinutes.
        /// </summary>
        private MatchResult CheckWinLoseConditions()
        {
            if (DateTime.UtcNow - _matchStartTime >= TimeSpan.FromMinutes(MaxMatchDurationMinutes))
                return MatchResult.Timeout;

            if (_players.FindAll(p => p.TeamId == 1).TrueForAll(p => !p.IsAlive))
                return MatchResult.Team0Win;

            if (_players.FindAll(p => p.TeamId == 0).TrueForAll(p => !p.IsAlive))
                return MatchResult.Team1Win;

            return MatchResult.Ongoing;
        }

        /// <summary>
        /// Snapshots the current world state into a GameStatePacket.
        /// Reads each PlayerAgent's position and copies to PlayerState.
        /// Stamps the current server tick counter.
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
                    TeamId = p.TeamId,
                    PosX = p.PosX,
                    PosY = p.PosY,
                    PosZ = p.PosZ,
                    RotYaw = p.RotYaw,
                    Hp = p.Hp,
                    IsAlive = p.IsAlive
                };
            }

            return new GameStatePacket { Tick = _tick, Players = playerStates };
        }

        // ─── Damage ───────────────────────────────────────────────────────────────

        private void TakeDamage(PlayerAgent player, float amount)
        {
            player.Hp = MathF.Max(0f, player.Hp - amount);
            if (player.Hp <= 0f && player.IsAlive)
            {
                player.IsAlive = false;
                // TODO: _physics.Bodies.Remove(player.PhysicsBodyHandle)
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
        Team0Win,
        Team1Win,
        Timeout
    }
}
