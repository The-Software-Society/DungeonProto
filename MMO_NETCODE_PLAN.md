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

**REVISED after code audit — NO interest-management rewrite needed.** The eruption has NO
`NetworkServer.Spawn`'d entities. Every consequence is client-side presentation toggled by the
phase: `EmberAtmosphere` (look), `EruptionSpawns` (lava-hazard scene content), `VolcanoPlume`,
`WorldStateGate`, and the eruption/floor-cleared cutscenes. So per-player phasing is a contained
change, not the deep instancing system.

**Build:**
1. **`PlayerWorldPhase`** — a per-player NetworkBehaviour with a `[SyncVar] Phase` (Calm/Erupting/
   Cleared), server-owned, driven by that player's quest progress. Party members stay in sync
   because quest progress already fans out.
2. **Redirect presentation to the LOCAL player's phase.** `EmberfallWorldState` becomes a
   per-client presentation hub whose `CurrentPhase` mirrors the local player's `PlayerWorldPhase`
   (instead of a global SyncVar + boss-death trigger). Its existing `OnPhaseChanged` listeners
   (`EruptionSpawns`, `VolcanoPlume`, `WorldStateGate`, `EmberAtmosphere`, cutscenes) then all react
   per-player. Town players (Calm) get calm sky + no cutscene.
3. **Triggers become per-player/party:** `EruptionTrigger` sets the entering player's (+ party's)
   phase to Erupting; the floor-boss death sets the killer's (+ party's) phase to Cleared.
4. **One gameplay nuance — `LavaHazard` damage.** Lava content is client-side, but its damage runs
   through `Health.TakeDamage` (server-only). With per-player phase, the SERVER must apply lava
   damage only to players who are (a) standing in lava AND (b) in the Erupting phase — a small
   server-side per-player phase check (the server reads each player's `PlayerWorldPhase` SyncVar).
5. `ServerProgressState` (server-wide floor-clear / first-clear prestige) stays GLOBAL — that's
   legitimately shared, independent of a player's personal eruption phase.

**Risk:** contained (no interest management), but touches the world-state system + lava damage, so
it wants a live 2-client sanity check (player A in Erupting sees fire/lava + takes lava damage;
player B in town sees calm + no cutscene). Recommend as the focused next build.
