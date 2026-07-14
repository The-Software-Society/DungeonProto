# Emberfall Asset Catalog

Running inventory of Meshy-generated assets. Meshes live in `Assets/Models/Props/{Name}.fbx`
(+ `.glb` twin + `_baseColor` texture); characters get rigged visual prefabs in
`Assets/Prefabs/Generated/{Name}Visual.prefab`. New props are defined as a name→prompt entry
in `tools/meshy_pipeline.py` (PROPS dict) and generated with `python3 tools/meshy_pipeline.py --props`
(resumable; skips already-generated). Place in the world with `PlaceMeshyProp(parent, name, pos,
yaw, heightMeters, groundLayer, collision)` — returns null if the fbx is missing, so callers
degrade gracefully. Props now snap to the terrain surface automatically.

**Convention (Jon):** structural "cube" for collision/mass where needed, with a real Meshy mesh
skinned on top for the visual (e.g. building = cube shell + Meshy roof/facade).

## Characters (rigged, animated)
Hero, HeroMale, HeroFemale · Aldric (quest giver) · Daughter (rescue) · Goblin · GoblinDancer ·
Bandit · OrcBrute · CaveTroll (boss) · EmberAcolyte · StoneSentinel
→ used via `SceneSetup.InstantiateGeneratedVisual` + `{Name}Visual` prefabs, server-spawned at runtime.

## Environment — trees & foliage
| Asset | Use | Status |
|---|---|---|
| PineTree, OakTree, BirchTree | forest canopy (ScatterForestTrees kit) | ✅ in use |
| DeadTree | charred trees toward the volcano | ✅ in use |
| ForestBush, TreeStump | undergrowth | ✅ in use (bush = fern fallback) |
| GrassSquare | grass tuft clumps | ✅ |
| Fern | forest undergrowth (ScatterGroundDetail) | ⏳ generating |
| Reeds | lake/river-edge water foliage | ⏳ generating |
| Wildflowers | sunlit meadow/glade | ⏳ generating |

## Environment — rock & ground
| Asset | Use | Status |
|---|---|---|
| MossBoulder, BoulderCluster, MossyRock | scattered boulders; waterfall rock outcrops | ✅ in use |
| RockCliff | waterfall/cliff faces | ✅ in use |
| PebblePatch | dirt/gravel ground detail | ✅ landed (wire next) |
| LavaRockGround | black basalt + glowing lava seams (volcanic approach) | ⏳ generating |

## Environment — mountains & volcano (NEW — replacing 'landfill hill' terrain)
| Asset | Use | Status |
|---|---|---|
| MountainPeak | single rugged rocky peak (border range) | ⏳ generating |
| MountainRidge | multi-peak ridge section (border backdrop) | ⏳ generating |
| ForestFoothill | green→rock transition foothill (valley side) | ⏳ generating |
| VolcanoDormant | dormant cone, green lower slopes (start state, ref img17) | ⏳ generating |
| VolcanoErupting | erupting cone, lava flows (questline state, ref img16) | ⏳ generating |

## Buildings & town props
Cottage, TownHouse, Storehouse, BlacksmithShop · TownWell, WellFountain, Statue · MarketStall,
MarketBarrel, WoodCrate, Signpost · TreasureChest, DungeonChest, DungeonPillar
| Asset | Use | Status |
|---|---|---|
| ThatchRoofSection, WoodShingleRoof | cube-shell + real-roof skinning for enterable buildings | ⏳ generating |

## Items / equipment / misc
KnightSword, RaiderSaber, RustySword, Emberfang · IronHelm, IronChestplate, IronLegs, LeatherCap,
BanditCape · MagicFocus · FishingPole, Fish · Campfire · HairShort, HairLong, HairPonytail

## Gaps to consider next
- Waterfall-specific cliff/gorge variations (currently reusing RockCliff + boulders).
- More building facade/roof variety for a denser New-World-style city.
- Snow/ice caps for the high mountains; dead/burnt shrubs for the volcanic band.
- Wildlife/critters for the ecosystem (currently only combat mobs).
