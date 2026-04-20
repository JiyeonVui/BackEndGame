namespace BackEndGame.Domain.Entities
{
    /// <summary>
    /// Represents one AI-controlled enemy inside an active match.
    /// Owns its DotRecast path state and BEPU physics body.
    /// Updated every tick by Match.TickEnemyAI().
    /// </summary>
    public class EnemyAgent
    {
        public byte EnemyId { get; set; }

        public float Hp { get; set; }
        public bool IsAlive { get; set; }

        /// <summary>
        /// Current AI behaviour state, mirrors the AnimState values sent in EnemyState packet.
        /// 0 = idle, 1 = patrol, 2 = attack, 3 = dead.
        /// </summary>
        public byte AnimState { get; set; }

        /// <summary>
        /// Handle to the BEPU rigid body for this enemy.
        /// Replace with the concrete BEPU type when integrating the package.
        /// </summary>
        public object? PhysicsBodyHandle { get; set; }

        /// <summary>
        /// Current DotRecast path that the enemy is following.
        /// Recomputed by the navmesh query whenever the enemy reaches a waypoint
        /// or a new target is selected in TickEnemyAI().
        /// </summary>
        public object? CurrentPath { get; set; }

        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }

        public float RotYaw { get; set; }

        /// <summary>World-space position the enemy is currently moving towards.</summary>
        public float TargetX { get; set; }
        public float TargetZ { get; set; }
    }
}
