namespace BackEndGame.Domain.Packets
{
    /// <summary>
    /// Broadcast by the server to ALL clients every tick (~64Hz).
    /// Contains the authoritative world snapshot: positions, HP, and anim states.
    /// Clients use this to interpolate rendering — no logic runs on client.
    /// </summary>
    public struct GameStatePacket
    {
        /// <summary>Server tick this snapshot was captured at. Client uses this to detect dropped packets.</summary>
        public uint Tick;

        public PlayerState[] Players;
        public EnemyState[] Enemies;
    }

    /// <summary>
    /// Authoritative state for one player in a tick snapshot.
    /// </summary>
    public struct PlayerState
    {
        public byte PlayerId;

        /// <summary>World-space position after BEPU physics step.</summary>
        public float PosX, PosY, PosZ;

        /// <summary>Horizontal facing angle in degrees.</summary>
        public float RotYaw;

        public float Hp;
        public bool IsAlive;
    }

    /// <summary>
    /// Authoritative state for one enemy in a tick snapshot.
    /// </summary>
    public struct EnemyState
    {
        public byte EnemyId;

        /// <summary>World-space position after DotRecast pathfind + BEPU step.</summary>
        public float PosX, PosY, PosZ;

        public float RotYaw;
        public float Hp;
        public bool IsAlive;

        /// <summary>
        /// Current animation state for the client to play.
        /// 0 = idle, 1 = patrol, 2 = attack, 3 = dead.
        /// </summary>
        public byte AnimState;
    }
}
