# MMO Netcode — remaining big items (plans)

Context: turning the co-op-trust netcode into MMO-grade netcode. Small/clear items
(boss-cinematic proximity scope, auto-attack visual sync, quest sequential step-gating) are
DONE. The two below are large, high-blast-radius changes that deserve dedicated, tested passes.

---

## #63 — Server-authoritative damage (anti-cheat)

**Problem:** the client computes the damage number and the server trusts it
(`PlayerCombat.CmdDealDamage`). A modified client can inflate damage.

**Why it's not a one-liner:** to *recompute* damage server-side, the server needs the
attacker's FULL combat state. Today only skill levels are synced to the server (`PlayerStatBridge`);
equipment modifiers (WeaponPower/SpellPower/Armor/AttackSpeed), the combat-stance mods, and
ability **mastery** ranks are all client-local. So the server can't currently reproduce the
damage formula.

**Two options:**

- **(A) Interim clamp-validate (low risk, fast).** Keep client-computed damage, but on the
  server clamp it to a plausible maximum derived from the server-known skill levels (+ a generous
  factor for gear/crits). Stops gross cheating (999999 dmg) with ZERO change to legit combat
  behavior. ~1 file (`PlayerCombat.CmdDealDamage`).
- **(B) Full recompute (the real thing).**
  1. Extend `PlayerStatBridge` to push the equipment+stance modifier TOTALS for the damage stats
     to the server (same Command pattern already used for skills), so the server StatBlock.Get is
     complete.
  2. Sync `AbilityMastery` ranks to the server (small).
  3. Change `PlayerCombat.DealDamage(precomputedNumber)` → `DealMelee/DealAbility/DealRanged(target,
     abilityId/xpSkill)`; the server runs `CombatMath` from its own StatBlock + ability def + mastery.
  4. Update call sites: `PlayerMelee`, `PlayerAbilities`, `Projectile`.
  Touches ~5-6 files; needs a live 2-client damage test to confirm numbers match. Higher risk.

**Recommendation:** ship (A) now for immediate anti-cheat, do (B) as a focused, tested task.

---

## #64 — Per-player / per-party world-state phasing (eruption follows the questline)

**Goal:** the eruption/cleared world-state should follow each player's (or party's) questline —
town players who haven't done the quest see Calm; the party that triggered it sees the eruption.
Today `EmberfallWorldState` is a single GLOBAL SyncVar phase (a deliberate "SAO server-wide floor"
choice) triggered by the floor-boss death, driving the atmosphere, spawns, and cutscenes for
EVERYONE. This is a design pivot from server-wide → phased.

**Layers (increasing depth):**
1. **Presentation phasing (doable, contained).** Move the phase to PER-PLAYER state driven by that
   player's quest progress. The eruption LOOK (`EmberAtmosphere` intensity) and the eruption/
   floor-cleared CUTSCENES key off the local player's phase. Town players never get them. Party
   members in sync (quest fan-out already keeps them in sync) reach the phase together.
2. **Entity phasing (the deep MMO part).** Eruption SPAWNS are shared entities; if player A is
   Erupting and B is Calm, they must see different mobs — per-observer network visibility
   (interest management by phase). Mirror doesn't do this natively; it's the same instancing/
   phasing system as the multi-world work (`MULTI_WORLD_PLAN.md`). Until built, spawns stay shared.

**Recommendation:** build layer 1 (per-player look + cutscene) as a focused task so the visible
"town shouldn't see it" problem is solved; schedule layer 2 with the instancing/phasing work,
since true per-player mobs require the interest-management system.
