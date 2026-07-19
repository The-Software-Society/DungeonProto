namespace AldenFall.World
{
    /// <summary>
    /// Canonical Emberfall Volcano (Floor 1 dungeon) geometry: the SINGLE SOURCE OF
    /// TRUTH shared by the editor builder (ArenaBuilderEmberfall) and the runtime
    /// death-reset safety envelope (DungeonResetController). Keeping both off these
    /// constants means a dungeon resize can never desync them and reopen the void
    /// under the room floors. The envelope is deliberately a little wider than the
    /// built inner walls so it always contains the playable space.
    /// </summary>
    public static class EmberfallVolcanoGeo
    {
        public const float EntranceZ = -1010f;   // north mouth (world z), beyond the extended approach
        public const float BossZ = -1480f;       // boss chamber center after a full multi-wing crawl
        public const float BackZ = -1535f;       // south back wall (behind the boss)
        public const float NorthZ = EntranceZ - 3f;   // envelope north face, just outside the mouth lintel (-318)

        public const float HalfWidth = 190f;     // supports side wings, loops and vertical chambers
        public const float FloorY = -0.55f;
        public const float CeilY = 8.5f;
        public const float WallCenterY = 4f;
        public const float WallHeight = 9f;
        public const float WallThickness = 2f;
        public const float EntranceHalfGap = 6f; // half of the 12m mouth on the north face

        // Derived envelope extents (used to build the reset safety slabs).
        public const float CenterZ = (NorthZ + BackZ) * 0.5f;  // -384
        public const float DepthZ = NorthZ - BackZ;            // 132
        public const float Width = HalfWidth * 2f;             // 112
        // North wall segments run from +-HalfWidth in to +-EntranceHalfGap, leaving the mouth open.
        public const float NorthSegWidth = HalfWidth - EntranceHalfGap;              // 50
        public const float NorthSegCenterX = (HalfWidth + EntranceHalfGap) * 0.5f;   // 31
    }
}
