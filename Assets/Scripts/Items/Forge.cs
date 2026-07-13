using AldenFall.Data;

namespace AldenFall.Items
{
    /// <summary>
    /// The gear-upgrade forge: combine two IDENTICAL items of the same +tier into
    /// one item a plus higher, for a gold fee. Builds directly on the ItemPlus
    /// layer (the "+N" suffix IS the instance) so the result flows through
    /// inventory / equipment untouched. Cost climbs steeply so late upgrades are
    /// a real gold sink.
    ///
    /// Economy scope: DoCombine mutates the passed Inventory + Wallet directly, which
    /// are the local player's. Authoritative in host mode (the beta path); on a
    /// dedicated server a pure client combines only its client-local copies, the same
    /// accepted limitation as VendorPanel / loot credit, resolved by the Phase 13
    /// dedicated-server economy-authority [Command] follow-up.
    /// </summary>
    public static class Forge
    {
        /// <summary>Two ids combine when they share a base item and plus tier, aren't
        /// already maxed, and are actual gear (upgrading food/tools is meaningless).</summary>
        public static bool CanCombine(string idA, string idB)
        {
            if (string.IsNullOrEmpty(idA) || string.IsNullOrEmpty(idB)) return false;
            if (ItemPlus.BaseId(idA) != ItemPlus.BaseId(idB)) return false;
            int plus = ItemPlus.PlusOf(idA);
            if (plus != ItemPlus.PlusOf(idB)) return false;
            if (plus >= ItemPlus.MaxPlus) return false;

            var def = ItemDatabase.Get(idA);
            return def != null && def.Slot != EquipSlot.None;
        }

        /// <summary>Gold cost to forge from the given plus to plus+1 (default 50, 200, 450, 800, …).
        /// Curve = forgeCostCoef * (plus+1)^forgeCostExp, from config/economy.json.</summary>
        public static int CostFor(int plus)
        {
            var e = GameConfig.Economy;
            int p1 = plus + 1;
            int pow = 1;
            for (int i = 0; i < e.forgeCostExp; i++) pow *= p1;
            return e.forgeCostCoef * pow;
        }

        /// <summary>The resulting id (base at plus+1), or null if the pair can't combine.</summary>
        public static string Combine(string idA, string idB) =>
            CanCombine(idA, idB)
                ? ItemPlus.Compose(ItemPlus.BaseId(idA), ItemPlus.PlusOf(idA) + 1)
                : null;

        /// <summary>
        /// Validates, charges gold, consumes both inputs, and grants the upgrade.
        /// All-or-nothing: a failure spends nothing and consumes nothing.
        /// </summary>
        public static bool DoCombine(Inventory inv, Wallet wallet, string idA, string idB)
        {
            if (inv == null || !CanCombine(idA, idB)) return false;

            // Need two real copies. Gear doesn't stack (MaxStack 1), so identical
            // ids occupy two cells; distinct plus-equal ids need one of each.
            if (idA == idB)
            {
                if (inv.CountOf(idA) < 2) return false;
            }
            else if (inv.CountOf(idA) < 1 || inv.CountOf(idB) < 1)
            {
                return false;
            }

            string result = Combine(idA, idB);
            if (result == null) return false;

            int cost = CostFor(ItemPlus.PlusOf(idA));
            if (wallet == null || !wallet.TrySpend(cost)) return false;

            inv.Remove(idA);
            inv.Remove(idB);

            if (!inv.Add(result))
            {
                // Bag full for the upgrade, refund everything rather than destroy value.
                inv.Add(idA);
                inv.Add(idB);
                wallet.Add(cost);
                return false;
            }
            return true;
        }
    }
}
