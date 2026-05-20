namespace BackEndGame.Domain.Packets
{
    /// <summary>
    /// Broadcast by the server to ALL clients every tick (~64Hz).
    /// Contains the authoritative world snapshot: positions, HP, and team info.
    /// Clients use this to interpolate rendering — no logic runs on client.
    /// </summary>
    public class GameStatePacket
    {
        public uint Tick { get; set; }
        public PlayerState[] Players { get; set; } = [];
    }

    public class PlayerState
    {
        public byte PlayerId { get; set; }
        public byte TeamId { get; set; }
        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }
        public float RotYaw { get; set; }
        public float Hp { get; set; }
        public bool IsAlive { get; set; }
    }
}
