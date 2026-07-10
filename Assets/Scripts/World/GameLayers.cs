using UnityEngine;

namespace AldenFall
{
    /// <summary>
    /// Central layer/mask registry. Every system resolves layers through here
    /// so there is exactly one place where layer assumptions live.
    /// Falls back to Default when a named layer is missing from the project.
    /// </summary>
    public static class GameLayers
    {
        private static bool initialized;

        public static int Ground { get; private set; }
        public static int Player { get; private set; }
        public static int Enemy { get; private set; }
        public static int Projectile { get; private set; }
        public static int Water { get; private set; }

        /// <summary>Layers projectiles/vision treat as solid world geometry.</summary>
        public static LayerMask WorldMask { get; private set; }
        /// <summary>Layers that can be targeted/damaged by the player.</summary>
        public static LayerMask EnemyMask { get; private set; }
        /// <summary>Layers enemies scan for the player.</summary>
        public static LayerMask PlayerMask { get; private set; }
        /// <summary>Swimmable water trigger volumes.</summary>
        public static LayerMask WaterMask { get; private set; }
        /// <summary>Ground for foot checks (Ground + Default).</summary>
        public static LayerMask GroundMask { get; private set; }

        private static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;

            Ground = Find("Ground");
            Player = Find("Player");
            Enemy = Find("Enemy");
            Projectile = Find("Projectile");
            Water = Find("Water");

            GroundMask = Mask(Ground) | 1; // Ground + Default
            WorldMask = GroundMask;
            EnemyMask = Mask(Enemy) | 1;   // Enemy + Default fallback
            PlayerMask = Mask(Player);
            if (PlayerMask == 0) PlayerMask = 1;
            WaterMask = Mask(Water);
        }

        private static int Find(string name)
        {
            int layer = LayerMask.NameToLayer(name);
            return layer >= 0 ? layer : 0;
        }

        private static LayerMask Mask(int layer) => layer > 0 ? (1 << layer) : 0;

        // Accessors that guarantee init before use
        public static LayerMask GetWorldMask() { EnsureInitialized(); return WorldMask; }
        public static LayerMask GetEnemyMask() { EnsureInitialized(); return EnemyMask; }
        public static LayerMask GetPlayerMask() { EnsureInitialized(); return PlayerMask; }
        public static LayerMask GetWaterMask() { EnsureInitialized(); return WaterMask; }
        public static LayerMask GetGroundMask() { EnsureInitialized(); return GroundMask; }
        public static int GetEnemyLayer() { EnsureInitialized(); return Enemy; }
        public static int GetPlayerLayer() { EnsureInitialized(); return Player; }
        public static int GetProjectileLayer() { EnsureInitialized(); return Projectile; }
    }
}
