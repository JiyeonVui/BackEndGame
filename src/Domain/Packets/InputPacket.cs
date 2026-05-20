namespace BackEndGame.Domain.Packets
{
    /// <summary>
    /// Sent by the Unity client to the server every tick (~64Hz).
    /// Contains all player input for a single simulation step.
    /// The server uses this to drive movement, shooting, and action logic.
    /// </summary>
    public class InputPacket
    {
        public uint Tick { get; set; }
        public byte PlayerId { get; set; }
        public float MoveX { get; set; }
        public float MoveZ { get; set; }
        public float LookYaw { get; set; }
        public float LookPitch { get; set; }
        public bool IsShooting { get; set; }
        public bool IsJumping { get; set; }
        public bool IsCrouching { get; set; }
        public bool IsSprinting { get; set; }
    }
}
