namespace BackEndGame.Domain.Entities
{
    /// <summary>
    /// Represents(đại diện cho) one player inside an active match.
    /// Holds the runtime state that the server mutates each tick:
    /// physics body reference, HP, and last known input.
    /// </summary>
    public class PlayerAgent
    {
        /// <summary>Matches the PlayerId sent in InputPacket / PlayerState packets.</summary>
        public byte PlayerId { get; set; }

        /// <summary>0 = Team A, 1 = Team B. Used by ProcessWeaponFiring to prevent friendly fire.</summary>
        public byte TeamId { get; set; }

        /// <summary>Linked to the User entity for stat reporting to PlayFab after match ends.</summary>
        public string DeviceId { get; set; } = string.Empty;

        public float Hp { get; set; }
        public bool IsAlive { get; set; }

        /// <summary>
        /// Handle to the BEPU capsule body for this player.
        /// Set during Match.SpawnPlayers() and used each tick in ProcessPlayerInputs().
        /// Type is object to avoid a hard compile dependency on BEPUphysics before the package is added.
        /// Replace with the concrete() BEPU BodyHandle / BodyReference type when integrating(tích hợp).
        /// </summary>
        public object? PhysicsBodyHandle { get; set; }

        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }

        public float RotYaw { get; set; }
        public float RotPitch { get; set; }
    }
}
