namespace BackEndGame.Domain.Packets
{
    /// <summary>
    /// Broadcast by the server to ALL clients every tick (~64Hz).
    /// Contains the authoritative world snapshot: positions, HP, and team info.
    /// Clients use this to interpolate rendering — no logic runs on client.
    /// </summary>
    public struct GameStatePacket
    {
        /// <summary>Server tick this snapshot was captured at. Client uses this to detect dropped packets.</summary>
        public uint Tick;

        public PlayerState[] Players;
    }

    /// <summary>
    /// Authoritative state for one player in a tick snapshot.
    /// </summary>
    public struct PlayerState
    {
        public byte PlayerId;

        /// <summary>0 = Team A, 1 = Team B. Client uses this to colour player models and determine hit validity.</summary>
        public byte TeamId;

        /// <summary>World-space position after BEPU physics step.</summary>
        public float PosX, PosY, PosZ;

        /// <summary>Horizontal facing angle in degrees.</summary>
        public float RotYaw;

        public float Hp;
        public bool IsAlive;
    }
}
