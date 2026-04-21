namespace BackEndGame.Domain.Packets
{
    /// <summary>
    /// Sent by the Unity client to the server every tick (~64Hz).
    /// Contains all player input for a single simulation step.
    /// The server uses this to drive movement, shooting, and action logic.
    /// </summary>
    public struct InputPacket
    {
        /// <summary>Monotonically increasing tick counter from the client, used for reconciliation (Đôi chiếu).</summary>
        public uint Tick;

        /// <summary>Identifies which player sent this input.</summary>
        public byte PlayerId;

        /// <summary>Horizontal movement axis (-1 to 1). Mapped to BEPU capsule velocity X.</summary>
        public float MoveX;

        /// <summary>Forward/backward movement axis (-1 to 1). Mapped to BEPU capsule velocity Z.</summary>
        public float MoveZ;

        /// <summary>Yaw (left/right) look angle in degrees.</summary>
        public float LookYaw;

        /// <summary>Pitch (up/down) look angle in degrees, clamped on server to prevent cheating.</summary>
        public float LookPitch;

        public bool IsShooting;
        public bool IsJumping;
        public bool IsCrouching;
        public bool IsSprinting;
    }
}
